using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Agent;

/// <summary>
/// Builds the context-window panel data. Every number is sourced from Anthropic:
/// <list type="bullet">
///   <item><b>Total</b> = last assistant message's persisted <c>InputTokens + CacheReadTokens + CacheCreationTokens</c>.</item>
///   <item>Segments are derived by subtraction from persisted per-turn totals where possible,
///         and from <see cref="ITokenCounter"/> (<c>/v1/messages/count_tokens</c>) for the few text-to-token
///         conversions that can't be inferred (first user message, current system prompt, summaries).</item>
/// </list>
/// Counter results are cached by content hash — repeated ctx clicks against unchanged text
/// don't hit the network. A segment whose counter call failed is emitted with <c>Tokens = -1</c>
/// so the UI can render "—"; we never silently substitute a local tokenizer.
/// </summary>
public sealed class ContextBreakdownService(
	IServiceScopeFactory  scopeFactory,
	Kernel                kernel,
	PromptComposer        promptComposer,
	AgentService          agentService,
	ToolPolicyService     toolPolicyService,
	ITokenCounter?        tokenCounter = null) {

	public sealed record Segment(string Name, int Tokens);
	public sealed record Breakdown(
		int                    Total,
		int                    Budget,
		IReadOnlyList<Segment> Segments,
		string                 Model,
		string?                Effort);

	private readonly ConcurrentDictionary<string, int> countCache = new();

	public async Task<Breakdown?> ComputeAsync(string chatId, CancellationToken ct, bool forceRefresh = false) {
		if (forceRefresh) this.countCache.Clear();
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity? chat = await db.Chats
			.Include(c => c.CurrentSession)
			.Include(c => c.AllowedSenders)
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == chatId, ct);
		if (chat?.CurrentSession is null) return null;

		AgentEntity   agent         = await agentService.ResolveForChatAsync(db, chat, ct);
		ModelSettings modelSettings = AgentService.ToModelSettings(agent);
		SessionEntity session       = chat.CurrentSession;
		SessionEntity? parentSession = session.ParentSessionId is not null
			? await db.Sessions.FindAsync([session.ParentSessionId], ct)
			: null;

		// Authoritative total: the last assistant message's API-reported input-side
		// token count. Anything else (summing segments, recounting locally) would be
		// an estimate, and we already have the exact number.
		MessageEntity? lastAssistant = await db.Messages
			.Where(m => m.SessionId == session.Id && m.Role == "assistant" && !m.Queued)
			.OrderByDescending(m => m.Timestamp)
			.FirstOrDefaultAsync(ct);

		int actualTotal = (lastAssistant?.InputTokens         ?? 0)
		                + (lastAssistant?.CacheReadTokens     ?? 0)
		                + (lastAssistant?.CacheCreationTokens ?? 0);

		int budget = modelSettings.ContextBudget;
		string model = modelSettings.Model;

		string systemPrompt = await promptComposer.BuildAsync(db, agent.Id, ct, chat, agent);

		// Fresh chat — no assistant turn has happened. Count the current system prompt
		// (one counter call, cached) and report the rest as zero. Never invent numbers
		// for content that doesn't exist yet.
		if (lastAssistant is null) {
			int? sysOnly = await this.CountSystemAsync(model, systemPrompt, ct);
			List<Segment> fresh = new() {
				new Segment("System prompt", sysOnly ?? -1),
				new Segment("Free space",    budget),
			};
			return new Breakdown(0, budget, fresh, agent.Model, agent.ThinkingEffort);
		}

		// First-turn anchor: total[1] = system + tools + firstUserMessage.
		// Knowing tokens(firstUserMessage) locks in (system + tools) for the whole
		// session — no extra counter call needed across subsequent turns.
		MessageEntity? firstAssistant = await db.Messages
			.Where(m => m.SessionId == session.Id && m.Role == "assistant" && !m.Queued
			         && m.InputTokens != null)
			.OrderBy(m => m.Timestamp)
			.FirstOrDefaultAsync(ct);

		MessageEntity? firstUser = await db.Messages
			.Where(m => m.SessionId == session.Id && m.Role == "user" && !m.Queued
			         && m.Body != null)
			.OrderBy(m => m.Timestamp)
			.FirstOrDefaultAsync(ct);

		int? systemTokens  = await this.CountSystemAsync(model, systemPrompt, ct);
		int? summaryTokens = await this.CountSummariesAsync(model, parentSession?.Summary, session.Summary, ct);

		int? systemPlusTools = null;
		if (firstAssistant is not null && firstUser?.Body is { Length: > 0 } firstUserBody) {
			int total1 = (firstAssistant.InputTokens         ?? 0)
			           + (firstAssistant.CacheReadTokens     ?? 0)
			           + (firstAssistant.CacheCreationTokens ?? 0);
			int? firstUserTokens = await this.CountTextAsync(model, firstUserBody, ct);
			if (firstUserTokens is int u && total1 > 0)
				systemPlusTools = Math.Max(0, total1 - u);
		}

		// Drift detection — agent / tool profile / system prompt changed since turn 1
		// would make the historical (system+tools) wrong. If the derived value is
		// smaller than the current system prompt alone, it's stale — recompute live.
		if (systemPlusTools is int spt && systemTokens is int st && spt < st) {
			systemPlusTools = await this.CountSystemAndToolsAsync(model, systemPrompt, agent, ct);
		}

		// Last-ditch fallback when turn-1 anchor is unusable (chat seeded by an
		// assistant, or the first user message has no Body): one extra counter call,
		// cached by hash of (system + filtered tool set).
		systemPlusTools ??= await this.CountSystemAndToolsAsync(model, systemPrompt, agent, ct);

		int? toolTokens = (systemPlusTools is int s && systemTokens is int sys)
			? Math.Max(0, s - sys)
			: (int?)null;

		int? messageTokens = (systemPlusTools is int spt2 && summaryTokens is int sumT)
			? Math.Max(0, actualTotal - spt2 - sumT)
			: (int?)null;

		int freeSpace = Math.Max(0, budget - actualTotal);

		// A segment whose counter call failed gets Tokens = -1 so the UI can render "—".
		// Never substitute a local tokenizer here — that's the bug we just fixed.
		List<Segment> segments = new() {
			new Segment("Messages",      messageTokens ?? -1),
			new Segment("Tools",         toolTokens    ?? -1),
			new Segment("System prompt", systemTokens  ?? -1),
			new Segment("Summaries",     summaryTokens ?? -1),
			new Segment("Free space",    freeSpace),
		};

		return new Breakdown(actualTotal, budget, segments, agent.Model, agent.ThinkingEffort);
	}

	// ── Counter helpers ─────────────────────────────────────────────
	// All four helpers share the same baseline (an empty-content user message)
	// and the same in-memory cache keyed by content hash. Returns null on any
	// counter failure so the caller can render "—" instead of a wrong number.

	private async Task<int?> CountTextAsync(string model, string text, CancellationToken ct) {
		int? baseline = await this.GetBaselineAsync(model, ct);
		if (baseline is null) return null;
		JsonObject body = new() {
			["model"]    = model,
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = text }
			}
		};
		int? raw = await this.CachedCountAsync("text:" + model + ":" + text, body, ct);
		return raw is null ? null : Math.Max(0, raw.Value - baseline.Value);
	}

	private async Task<int?> CountSystemAsync(string model, string systemPrompt, CancellationToken ct) {
		if (string.IsNullOrEmpty(systemPrompt)) return 0;
		int? baseline = await this.GetBaselineAsync(model, ct);
		if (baseline is null) return null;
		JsonObject body = new() {
			["model"]    = model,
			["system"]   = new JsonArray {
				new JsonObject { ["type"] = "text", ["text"] = systemPrompt }
			},
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = "" }
			}
		};
		int? raw = await this.CachedCountAsync("system:" + model + ":" + systemPrompt, body, ct);
		return raw is null ? null : Math.Max(0, raw.Value - baseline.Value);
	}

	private async Task<int?> CountSummariesAsync(string model, string? parent, string? current, CancellationToken ct) {
		bool hasParent  = parent  is { Length: > 0 };
		bool hasCurrent = current is { Length: > 0 };
		if (!hasParent && !hasCurrent) return 0;
		// One call covering both — fewer roundtrips and the envelope cancels via baseline.
		string combined = (hasParent ? parent! : "")
		                + (hasParent && hasCurrent ? "\n" : "")
		                + (hasCurrent ? current! : "");
		return await this.CountTextAsync(model, combined, ct);
	}

	/// <summary>
	/// Counts system + the agent's filtered tool set, via a single count_tokens call
	/// using the same Anthropic wire format <see cref="ChatHistorySerializer"/> emits.
	/// Used when the turn-1 anchor is unusable or stale.
	/// </summary>
	private async Task<int?> CountSystemAndToolsAsync(string model, string systemPrompt, AgentEntity agent, CancellationToken ct) {
		int? baseline = await this.GetBaselineAsync(model, ct);
		if (baseline is null) return null;

		string? origProfile = agent.ToolProfile;
		agent.ToolProfile = agent.ToolProfile ?? "standard";
		HashSet<string> allowed = toolPolicyService.GetAllowedPluginNames(agent);
		agent.ToolProfile = origProfile;

		// Clone the kernel and strip plugins the current agent isn't allowed to use,
		// so the tool array we serialize matches the one the live call would carry.
		Kernel filtered = kernel.Clone();
		foreach (string name in filtered.Plugins.Select(p => p.Name).Where(n => !allowed.Contains(n)).ToList())
			filtered.Plugins.Remove(filtered.Plugins[name]);

		JsonArray tools = ChatHistorySerializer.BuildToolsArray(filtered);

		JsonObject body = new() {
			["model"]    = model,
			["system"]   = new JsonArray {
				new JsonObject { ["type"] = "text", ["text"] = systemPrompt }
			},
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = "" }
			},
			["tools"]    = tools
		};

		// Cache key incorporates the tool set fingerprint so a profile/agent change
		// naturally misses the cache and re-counts.
		string toolFp = tools.ToJsonString();
		int? raw = await this.CachedCountAsync("sysTools:" + model + ":" + systemPrompt + ":" + toolFp, body, ct);
		return raw is null ? null : Math.Max(0, raw.Value - baseline.Value);
	}

	private async Task<int?> GetBaselineAsync(string model, CancellationToken ct) {
		JsonObject body = new() {
			["model"]    = model,
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = "" }
			}
		};
		return await this.CachedCountAsync("baseline:" + model, body, ct);
	}

	private async Task<int?> CachedCountAsync(string keyMaterial, JsonObject body, CancellationToken ct) {
		if (tokenCounter is null) return null;
		string key = Hash(keyMaterial);
		if (this.countCache.TryGetValue(key, out int cached)) return cached;
		int? result = await tokenCounter.CountAsync(body.ToJsonString(), ct);
		if (result is int v) this.countCache[key] = v;
		return result;
	}

	private static string Hash(string s) {
		byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
		return Convert.ToHexString(bytes);
	}
}
