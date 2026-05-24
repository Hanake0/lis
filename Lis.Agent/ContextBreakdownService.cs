using System.Text.Json;

using Lis.Core.Configuration;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

/// <summary>
/// Builds the context-window panel data. The <b>Total</b> shown to the user is the
/// authoritative input-side token count from the last assistant message's API response
/// (input + cache_read + cache_creation) — never a local estimate. The per-segment
/// breakdown still uses local tokenization since Anthropic doesn't return a breakdown,
/// matching what Claude Code shows in its own panel. Segments are advisory; the total
/// is the source of truth.
/// </summary>
public sealed class ContextBreakdownService(
	IServiceScopeFactory  scopeFactory,
	Kernel                kernel,
	PromptComposer        promptComposer,
	ContextWindowBuilder  contextWindowBuilder,
	AgentService          agentService,
	ToolPolicyService     toolPolicyService,
	IOptions<LisOptions>  options) {

	public sealed record Segment(string Name, int Tokens);
	public sealed record Breakdown(
		int                    Total,
		int                    Budget,
		IReadOnlyList<Segment> Segments,
		string                 Model,
		string?                Effort);

	public async Task<Breakdown?> ComputeAsync(string chatId, CancellationToken ct) {
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

		// Per-segment breakdown is locally tokenized — the API doesn't return one, so
		// this is the same approach Claude Code uses. The numbers won't sum exactly to
		// actualTotal (tokenizer ≈ API), but they show *what* makes up the context.
		List<MessageEntity> messages = await db.Messages
			.Where(m => m.SessionId == session.Id && !m.Queued)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, agent.Id, ct, chat, agent);

		ChatHistory history = contextWindowBuilder.Build(
			systemPrompt, messages, session, parentSession, options.Value, chat);

		int systemTokens = TokenEstimator.Count(systemPrompt);

		int summaryTokens =
			TokenEstimator.Count(parentSession?.Summary) +
			TokenEstimator.Count(session.Summary);

		int messageTokens = 0;
		int startIdx = string.IsNullOrWhiteSpace(systemPrompt) ? 0 : 1;
		int summarySlots = (parentSession?.Summary is { Length: > 0 } ? 1 : 0)
		                 + (session.Summary is { Length: > 0 } ? 1 : 0);
		for (int i = startIdx + summarySlots; i < history.Count; i++) {
			ChatMessageContent msg = history[i];
			messageTokens += TokenEstimator.Count(SerializeMessage(msg));
		}

		string? origProfile = agent.ToolProfile;
		agent.ToolProfile = agent.ToolProfile ?? "standard";
		HashSet<string> allowedPlugins = toolPolicyService.GetAllowedPluginNames(agent);
		agent.ToolProfile = origProfile;

		int toolTokens = 0;
		foreach (KernelPlugin plugin in kernel.Plugins) {
			if (!allowedPlugins.Contains(plugin.Name)) continue;
			foreach (KernelFunction fn in plugin) {
				toolTokens += TokenEstimator.Count(SerializeToolDefinition(plugin.Name, fn));
			}
		}

		// Free space sits against the *authoritative* total so the panel never lies
		// about how much room is left, even if local tokenization drifts.
		int freeSpace = Math.Max(0, budget - actualTotal);

		List<Segment> segments = new();
		if (messageTokens > 0) segments.Add(new Segment("Messages",      messageTokens));
		if (toolTokens    > 0) segments.Add(new Segment("Tools",         toolTokens));
		if (systemTokens  > 0) segments.Add(new Segment("System prompt", systemTokens));
		if (summaryTokens > 0) segments.Add(new Segment("Summaries",     summaryTokens));
		segments.Add(new Segment("Free space", freeSpace));

		return new Breakdown(actualTotal, budget, segments, agent.Model, agent.ThinkingEffort);
	}

	private static string SerializeMessage(ChatMessageContent msg) {
		if (!string.IsNullOrEmpty(msg.Content)) return msg.Content;
		try { return JsonSerializer.Serialize(msg.Items); }
		catch { return msg.Content ?? ""; }
	}

	private static string SerializeToolDefinition(string pluginName, KernelFunction fn) {
		KernelFunctionMetadata meta = fn.Metadata;
		var schema = new {
			name        = $"{pluginName}_{meta.Name}",
			description = meta.Description ?? "",
			parameters  = meta.Parameters.Select(p => new {
				name        = p.Name,
				description = p.Description ?? "",
				required    = p.IsRequired,
				type        = p.ParameterType?.Name ?? "string"
			}).ToArray()
		};
		return JsonSerializer.Serialize(schema);
	}
}
