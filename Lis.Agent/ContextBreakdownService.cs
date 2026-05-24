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
/// Computes a per-segment token breakdown of the prompt that would be sent to the
/// model for a chat right now. Uses the same components that build the live request
/// (PromptComposer, ContextWindowBuilder, ToolPolicyService) so the numbers reflect
/// what the next API call would actually carry, not a guess.
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
	public sealed record Breakdown(int Total, int Budget, IReadOnlyList<Segment> Segments);

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

		List<MessageEntity> messages = await db.Messages
			.Where(m => m.SessionId == session.Id && !m.Queued)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, agent.Id, ct, chat, agent);

		ChatHistory history = contextWindowBuilder.Build(
			systemPrompt, messages, session, parentSession, options.Value, chat);

		// System prompt sits at history[0] when non-empty.
		int systemTokens = TokenEstimator.Count(systemPrompt);

		// Session summaries (parent + current) are injected as the first assistant
		// messages by ContextWindowBuilder — count them by re-walking the source.
		int summaryTokens =
			TokenEstimator.Count(parentSession?.Summary) +
			TokenEstimator.Count(session.Summary);

		// Messages are everything in history minus the system prompt slot and the
		// summary slots we just counted. Serialize each remaining slot for tokenization.
		int messageTokens = 0;
		int startIdx = string.IsNullOrWhiteSpace(systemPrompt) ? 0 : 1;
		int summarySlots = (parentSession?.Summary is { Length: > 0 } ? 1 : 0)
		                 + (session.Summary is { Length: > 0 } ? 1 : 0);
		for (int i = startIdx + summarySlots; i < history.Count; i++) {
			ChatMessageContent msg = history[i];
			messageTokens += TokenEstimator.Count(SerializeMessage(msg));
		}

		// Tools — the function schemas attached to the Anthropic request. Filter by
		// the agent's tool profile so the count matches what the live call would carry.
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

		int total = systemTokens + summaryTokens + messageTokens + toolTokens;

		List<Segment> segments = new() {
			new Segment("System prompt",     systemTokens),
			new Segment("Memory / summaries", summaryTokens),
			new Segment("Messages",          messageTokens),
			new Segment("Tools",             toolTokens),
		};

		return new Breakdown(total, modelSettings.ContextBudget, segments);
	}

	private static string SerializeMessage(ChatMessageContent msg) {
		// Reasonable approximation — captures text, function calls, function results.
		// Doesn't account for image bytes (model-side, not tokenized as text).
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
