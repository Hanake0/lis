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
/// <summary>
/// Public contract for <see cref="ContextBreakdownService"/>. Extracted so the
/// REST controllers can be unit-tested with a fake without spinning the full
/// SK + kernel + token-counter dependency chain.
/// </summary>
public interface IContextBreakdownService {
	Task<ContextBreakdownService.Breakdown?> ComputeAsync(string chatId, CancellationToken ct, bool forceRefresh = false);
}

public sealed class ContextBreakdownService(
	IServiceScopeFactory  scopeFactory,
	Kernel                kernel,
	PromptComposer        promptComposer,
	AgentService          agentService,
	ToolPolicyService     toolPolicyService,
	ITokenCounter?        tokenCounter = null) :IContextBreakdownService {

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

		PromptComposer.ComposedPrompt composed = await promptComposer.BuildSectionsAsync(db, agent.Id, ct, chat, agent);
		string systemPrompt = ConcatPrompt(composed);

		// Per-section + memory file counts. Each gets its own count_tokens call (cached).
		// Tokenize all in parallel.
		Task<(string Name, int? Tokens)>[] sectionTasks = composed.Sections
			.Select(async p => (Name: p.Name, Tokens: await this.CountTextAsync(model, p.Content, ct)))
			.ToArray();
		Task<int?> memoriesTask = composed.Memories is { Length: > 0 } mem
			? this.CountTextAsync(model, mem, ct)
			: Task.FromResult<int?>(0);

		(string Name, int? Tokens)[] sectionCounts = await Task.WhenAll(sectionTasks);
		int? memoriesTokens = await memoriesTask;

		// Sum is needed both for drift detection and for the live tools-by-subtraction.
		// If any section count failed, we mark the sum unusable (null) so downstream
		// segments degrade to "—" rather than report a too-low system total.
		int? systemTokens = (sectionCounts.All(s => s.Tokens is not null) && memoriesTokens is not null)
			? sectionCounts.Sum(s => s.Tokens!.Value) + memoriesTokens.Value
			: (int?)null;

		// Compaction reserves the top of the budget for the summarizer. Show it
		// even on a fresh chat so the user sees what's actually available for
		// conversation tokens up-front.
		int freshThresholdPct = agent.CompactionThreshold > 0 ? agent.CompactionThreshold : 80;
		int freshCompactionBuffer = Math.Max(0, budget - (int)(budget * (freshThresholdPct / 100.0)));

		// Fresh chat — no assistant turn has happened. Show per-section breakdown
		// with Free space taking the rest. Tools is unknown (no anchor yet).
		if (lastAssistant is null) {
			List<Segment> fresh = new();
			foreach ((string name, int? tokens) in sectionCounts)
				fresh.Add(new Segment(name, tokens ?? -1));
			if (composed.Memories is { Length: > 0 })
				fresh.Add(new Segment("Memory files", memoriesTokens ?? -1));
			if (freshCompactionBuffer > 0)
				fresh.Add(new Segment("Compaction buffer", freshCompactionBuffer));
			fresh.Add(new Segment("Free space", Math.Max(0, budget - freshCompactionBuffer)));
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

		// Compaction reserves the top of the budget for the summarizer to work in —
		// this many tokens are unavailable to the conversation even before Free space.
		// Match the math ConversationService.MaybeRunCompactionAsync uses.
		int thresholdPct = agent.CompactionThreshold > 0 ? agent.CompactionThreshold : 80;
		int compactionAbsolute = (int)(budget * (thresholdPct / 100.0));
		int compactionBuffer = Math.Max(0, budget - compactionAbsolute);
		int freeSpace = Math.Max(0, budget - actualTotal - compactionBuffer);

		// A segment whose counter call failed gets Tokens = -1 so the UI can render "—".
		// Never substitute a local tokenizer here — that's the bug we just fixed.
		List<Segment> segments = new() {
			new Segment("Messages", messageTokens ?? -1),
			new Segment("Tools",    toolTokens    ?? -1),
		};
		foreach ((string name, int? tokens) in sectionCounts)
			segments.Add(new Segment(name, tokens ?? -1));
		if (composed.Memories is { Length: > 0 })
			segments.Add(new Segment("Memory files", memoriesTokens ?? -1));
		segments.Add(new Segment("Summaries", summaryTokens ?? -1));
		if (compactionBuffer > 0)
			segments.Add(new Segment("Compaction buffer", compactionBuffer));
		segments.Add(new Segment("Free space", freeSpace));

		return new Breakdown(actualTotal, budget, segments, agent.Model, agent.ThinkingEffort);
	}

	private static string ConcatPrompt(PromptComposer.ComposedPrompt composed) {
		StringBuilder sb = new();
		foreach (PromptComposer.PromptPart part in composed.Sections)
			sb.Append(part.Content);
		if (composed.Memories is { Length: > 0 } mem) sb.Append(mem);
		return sb.ToString();
	}

	// ── Counter helpers ─────────────────────────────────────────────
	// Each helper makes one count_tokens call (cached by content hash) and returns
	// the raw input_tokens. Anthropic's API 400s on empty user content, so when a
	// dummy message is needed (system-only / tools-only counts) we send "." — its
	// single token bleeds into the segment count by ~1, which is invisible against
	// segments measured in thousands. No baseline subtraction; same shape Claude
	// Code uses (see tokenEstimation.countTokensWithAPI, which 0-shorts on empty
	// content explicitly because the API rejects it). Returns null on counter
	// failure so the caller can render "—" — never substituted by a local tokenizer.

	private const string DummyContent = ".";

	private async Task<int?> CountTextAsync(string model, string text, CancellationToken ct) {
		if (string.IsNullOrEmpty(text)) return 0;
		JsonObject body = new() {
			["model"]    = model,
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = text }
			}
		};
		return await this.CachedCountAsync("text:" + model + ":" + text, body, ct);
	}

	private async Task<int?> CountSystemAsync(string model, string systemPrompt, CancellationToken ct) {
		if (string.IsNullOrEmpty(systemPrompt)) return 0;
		JsonObject body = new() {
			["model"]    = model,
			["system"]   = new JsonArray {
				new JsonObject { ["type"] = "text", ["text"] = systemPrompt }
			},
			["messages"] = new JsonArray {
				new JsonObject { ["role"] = "user", ["content"] = DummyContent }
			}
		};
		return await this.CachedCountAsync("system:" + model + ":" + systemPrompt, body, ct);
	}

	private async Task<int?> CountSummariesAsync(string model, string? parent, string? current, CancellationToken ct) {
		bool hasParent  = parent  is { Length: > 0 };
		bool hasCurrent = current is { Length: > 0 };
		if (!hasParent && !hasCurrent) return 0;
		// One call covering both — fewer roundtrips and identical envelope to other text counts.
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
				new JsonObject { ["role"] = "user", ["content"] = DummyContent }
			},
			["tools"]    = tools
		};

		// Cache key incorporates the tool set fingerprint so a profile/agent change
		// naturally misses the cache and re-counts.
		string toolFp = tools.ToJsonString();
		return await this.CachedCountAsync("sysTools:" + model + ":" + systemPrompt + ":" + toolFp, body, ct);
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
