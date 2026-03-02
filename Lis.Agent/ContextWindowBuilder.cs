using System.Text.Json;

using Lis.Persistence.Entities;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ContextWindowBuilder {
	// SK serializes $type discriminators after other properties; STJ requires them first by default.
	private static readonly JsonSerializerOptions SkJsonOptions = new() { AllowOutOfOrderMetadataProperties = true };

	public ChatHistory Build(
		string systemPrompt, IReadOnlyList<MessageEntity> messages,
		SessionEntity? session = null, SessionEntity? parentSession = null) {

		ChatHistory history = new(systemPrompt);

		// Inject parent session summary for continuity
		if (parentSession?.Summary is { Length: > 0 } parentSummary)
			history.AddAssistantMessage($"Here is context from the previous conversation session:\n{parentSummary}");

		// Inject current session summary (from earlier compaction in this session)
		if (session?.Summary is { Length: > 0 } summary)
			history.AddAssistantMessage($"Here is context from our earlier conversation:\n{summary}");

		// Add messages, applying tool pruning where needed
		foreach (MessageEntity msg in messages) {
			// Tool output pruning: replace tool results with one-liners for messages
			// before the prune boundary (non-destructive, DB unchanged)
			if (session?.ToolsPrunedThroughId is not null
			    && msg.Id <= session.ToolsPrunedThroughId
			    && msg.SkContent is not null) {

				ChatMessageContent? skMsg = JsonSerializer.Deserialize<ChatMessageContent>(msg.SkContent, SkJsonOptions);
				if (skMsg?.Role == AuthorRole.Tool) {
					// Extract function name from FunctionResultContent if available
					string funcName = skMsg.Items.OfType<FunctionResultContent>().FirstOrDefault()?.FunctionName ?? "tool";
					history.AddAssistantMessage($"[result: {funcName}]");
					continue;
				}
			}

			// Normal message deserialization
			if (msg.SkContent is not null) {
				ChatMessageContent? skMsg = JsonSerializer.Deserialize<ChatMessageContent>(msg.SkContent, SkJsonOptions);
				if (skMsg is not null) { history.Add(skMsg); continue; }
			}

			string content = msg.Body ?? "[media]";
			if (msg.IsFromMe) history.AddAssistantMessage(content);
			else              history.AddUserMessage(content);
		}

		SanitizeToolPairs(history);

		return history;
	}

	/// <summary>
	/// Strips orphaned tool_use/tool_result pairs from the history to prevent
	/// Anthropic's "tool_use without tool_result" validation error.
	/// Preserves message text content — only removes tool metadata.
	/// </summary>
	private static void SanitizeToolPairs(ChatHistory history) {
		// Remove dangling tool_use at the end (tool never completed, no useful context)
		while (history.Count > 1
		       && history[^1].Role == AuthorRole.Assistant
		       && history[^1].Items.OfType<FunctionCallContent>().Any()) {
			history.RemoveAt(history.Count - 1);
		}

		// Fix orphaned tool metadata in the rest of the history
		for (int i = 1; i < history.Count; i++) {
			ChatMessageContent msg = history[i];

			// Orphaned tool_result: walk backwards through consecutive Tool messages
			// to find the originating assistant with FunctionCallContent.
			// An assistant with N tool calls produces N consecutive Tool messages.
			if (msg.Role == AuthorRole.Tool) {
				bool valid = false;
				for (int j = i - 1; j >= 1; j--) {
					if (history[j].Role == AuthorRole.Tool) continue;
					valid = history[j].Role == AuthorRole.Assistant
					        && history[j].Items.OfType<FunctionCallContent>().Any();
					break;
				}

				if (!valid)
					history[i] = new ChatMessageContent(AuthorRole.Assistant, msg.Content);
			}

			// Orphaned tool_use: count FunctionCallContent items and verify
			// there are enough consecutive Tool messages to match all calls.
			if (msg.Role == AuthorRole.Assistant && msg.Items.OfType<FunctionCallContent>().Any()) {
				int callCount = msg.Items.OfType<FunctionCallContent>().Count();
				int resultCount = 0;
				for (int j = i + 1; j < history.Count && history[j].Role == AuthorRole.Tool; j++)
					resultCount++;

				if (resultCount < callCount) {
					List<FunctionCallContent> toRemove = msg.Items.OfType<FunctionCallContent>().ToList();
					foreach (FunctionCallContent item in toRemove) msg.Items.Remove(item);
				}
			}
		}
	}
}
