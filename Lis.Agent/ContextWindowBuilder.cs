using System.Text.Json;

using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ContextWindowBuilder {
	// SK serializes $type discriminators after other properties; STJ requires them first by default.
	private static readonly JsonSerializerOptions SkJsonOptions = new() { AllowOutOfOrderMetadataProperties = true };

	public ChatHistory Build(
		string systemPrompt, IReadOnlyList<MessageEntity> messages,
		SessionEntity? session = null, SessionEntity? parentSession = null,
		LisOptions? options = null, ChatEntity? chat = null) {

		ChatHistory history = string.IsNullOrWhiteSpace(systemPrompt) ? new() : new(systemPrompt);

		// Apply group context windowing — reduces noise from non-relevant messages
		if (chat is { IsGroup: true })
			messages = ApplyGroupWindowing(messages, chat.GroupContextMessages ?? options?.GroupContextMessages ?? 5);

		// Inject parent session summary for continuity
		if (parentSession?.Summary is { Length: > 0 } parentSummary)
			history.AddAssistantMessage($"Here is context from the previous conversation session:\n{parentSummary}");

		// Inject current session summary (from earlier compaction in this session)
		if (session?.Summary is { Length: > 0 } summary)
			history.AddAssistantMessage($"Here is context from our earlier conversation:\n{summary}");

		string policy = options?.ToolSummarizationPolicy ?? "auto";
		long? pruneBoundary = session?.ToolsPrunedThroughId;

		// Safeguard: keep_all yields to auto when pruning was triggered
		// (either by threshold or manual /prune) to prevent runaway context.
		if (policy == "keep_all" && pruneBoundary is not null)
			policy = "auto";

		// Compute keep boundary for ToolKeepThreshold — tool messages at or after this ID
		// skip pruning at build time even if they're before pruneBoundary.
		// Long.MaxValue means "keep nothing" (no threshold configured).
		long keepFromId = ComputeToolKeepBoundary(messages, pruneBoundary, options?.ToolKeepThreshold ?? 0);

		// Track prune boundary position for cache breakpoint #3
		int pruneBoundaryIdx = -1;

		// Add messages, applying tool pruning where needed
		foreach (MessageEntity msg in messages) {
			bool inPruneWindow = pruneBoundary is not null && msg.Id <= pruneBoundary;

			if (inPruneWindow && msg.SkContent is not null) {
				ChatMessageContent? skMsg = JsonSerializer.Deserialize<ChatMessageContent>(msg.SkContent, SkJsonOptions);
				if (skMsg?.Role == AuthorRole.Tool) {
					if (policy == "keep_all") {
						history.Add(skMsg);
					} else if (policy == "keep_none") {
						PruneToolResult(history, skMsg);
					} else if (msg.Id >= keepFromId) {
						// auto: ToolKeepThreshold keeps recent outputs unpruned
						history.Add(skMsg);
					} else if (msg.MediaData is not null && msg.MediaType is "image" or "sticker") {
				ChatMessageContent imgMsg = new(AuthorRole.User, content: (string?)null);
				if (msg.Body is { Length: > 0 } bodyText)
					imgMsg.Items.Add(new TextContent(bodyText));
				else if (msg.MediaCaption is { Length: > 0 } caption)
					imgMsg.Items.Add(new TextContent(caption));
				imgMsg.Items.Add(new ImageContent(msg.MediaData, msg.MediaMimeType ?? "image/jpeg"));
				history.Add(imgMsg);
			} else {
						// auto: check per-tool [ToolSummarization] attribute
						FunctionResultContent? frc = skMsg.Items.OfType<FunctionResultContent>().FirstOrDefault();
						if (frc is not null && HasSummarizePolicy(frc.FunctionName))
							history.Add(skMsg);
						else
							PruneToolResult(history, skMsg);
					}

					pruneBoundaryIdx = history.Count - 2; // -2: index 0 is system
					continue;
				}
			}

			// Normal message deserialization
			if (msg.SkContent is not null) {
				ChatMessageContent? skMsg = JsonSerializer.Deserialize<ChatMessageContent>(msg.SkContent, SkJsonOptions);
				if (skMsg is not null) { history.Add(skMsg); }
				else { history.AddAssistantMessage(msg.Body ?? "[media]"); }
			} else if (msg.MediaData is not null && msg.MediaType is "image" or "sticker") {
				ChatMessageContent imgMsg = new(AuthorRole.User, content: (string?)null);
				string imgText = msg.Body is { Length: > 0 } bodyText ? bodyText
					: msg.MediaCaption is { Length: > 0 } caption ? caption
					: "[image]";
				imgMsg.Items.Add(new TextContent(UserPrefix(msg) + imgText));
				imgMsg.Items.Add(new ImageContent(msg.MediaData, msg.MediaMimeType ?? "image/jpeg"));
				history.Add(imgMsg);
			} else {
				string body = msg.Body ?? msg.MediaCaption ?? "[media]";
				if (msg.IsFromMe) history.AddAssistantMessage(body);
				else              history.AddUserMessage(UserPrefix(msg) + body);
			}

			if (inPruneWindow)
				pruneBoundaryIdx = history.Count - 2;
		}

		// Communicate prune boundary to CacheControlHandler via AsyncLocal
		ToolContext.CacheBreakIndex = pruneBoundaryIdx;

		SanitizeToolPairs(history);

		return history;
	}

	private static string UserPrefix(MessageEntity msg) =>
		msg.SenderName is { Length: > 0 } name
			? $"[{msg.Id}] {name}: "
			: $"[{msg.Id}] ";

	private static void PruneToolResult(ChatHistory history, ChatMessageContent skMsg) {
		FunctionResultContent? original = skMsg.Items.OfType<FunctionResultContent>().FirstOrDefault();
		if (original is null) {
			// Fallback: no FunctionResultContent metadata — add as bare tool message
			history.Add(new ChatMessageContent(AuthorRole.Tool, "pruned"));
			return;
		}

		ChatMessageContent pruned = new(AuthorRole.Tool, content: (string?)null);
		pruned.Items.Add(new FunctionResultContent(original.FunctionName, original.PluginName, original.CallId, original.FunctionName));
		history.Add(pruned);
	}

	private static long ComputeToolKeepBoundary(
		IReadOnlyList<MessageEntity> messages, long? pruneBoundary, int keepThreshold) {
		if (pruneBoundary is null || keepThreshold <= 0) return long.MaxValue; // no keep window

		int accumulated = 0;
		long keepFrom = long.MaxValue;
		// Walk from newest message backwards within prune window, accumulate tool output sizes
		for (int i = messages.Count - 1; i >= 0; i--) {
			MessageEntity m = messages[i];
			if (m.Id > pruneBoundary) continue; // not in prune window
			if (m.Role is not "tool") continue;

			accumulated += m.OutputTokens ?? 0;
			if (accumulated > keepThreshold)
				return keepFrom; // return the ID of the last tool that fit
			keepFrom = m.Id;
		}

		return keepFrom; // everything fits within threshold — keep all tools
	}

	/// <summary>
	/// Filters group messages to reduce noise. Keeps all bot messages (assistant/tool)
	/// and limits non-bot messages to the last N before each bot response.
	/// </summary>
	internal static IReadOnlyList<MessageEntity> ApplyGroupWindowing(
		IReadOnlyList<MessageEntity> messages, int keepCount) {
		if (keepCount <= 0 || messages.Count == 0) return messages;

		// Find indices of "relevant" messages (bot turns: assistant/tool or IsFromMe)
		HashSet<int> keep = new();
		for (int i = 0; i < messages.Count; i++) {
			MessageEntity msg = messages[i];
			if (msg.IsFromMe || msg.Role is "assistant" or "tool")
				keep.Add(i);
		}

		// For each relevant message, walk backwards and keep up to N non-relevant messages before it
		foreach (int idx in keep.ToList()) {
			int kept = 0;
			for (int j = idx - 1; j >= 0 && kept < keepCount; j--) {
				if (keep.Contains(j)) break; // hit another relevant message, stop
				keep.Add(j);
				kept++;
			}
		}

		// Keep last N messages after the final bot response (trailing user messages)
		if (messages.Count > 0) {
			int kept = 0;
			for (int j = messages.Count - 1; j >= 0 && kept < keepCount; j--) {
				if (keep.Contains(j)) break;
				keep.Add(j);
				kept++;
			}
		}

		List<MessageEntity> filtered = new(keep.Count);
		for (int i = 0; i < messages.Count; i++)
			if (keep.Contains(i))
				filtered.Add(messages[i]);

		return filtered;
	}

	private static bool HasSummarizePolicy(string? functionName) {
		if (functionName is null) return false;
		// Check all loaded assemblies for KernelFunction methods with ToolSummarization attribute
		foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
			try {
				foreach (Type type in asm.GetTypes()) {
					foreach (System.Reflection.MethodInfo method in type.GetMethods()) {
						KernelFunctionAttribute? kf = method.GetCustomAttributes(typeof(KernelFunctionAttribute), false)
							.OfType<KernelFunctionAttribute>().FirstOrDefault();
						if (kf?.Name == functionName) {
							ToolSummarizationAttribute? ts = method.GetCustomAttributes(typeof(ToolSummarizationAttribute), false)
								.OfType<ToolSummarizationAttribute>().FirstOrDefault();
							return ts?.Policy == SummarizationPolicy.Summarize;
						}
					}
				}
			} catch {
				// Skip assemblies that can't be reflected
			}
		}

		return false;
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
					HashSet<string> matchedIds = new();
					for (int j = i + 1; j < history.Count && history[j].Role == AuthorRole.Tool; j++) {
						FunctionResultContent? frc = history[j].Items.OfType<FunctionResultContent>().FirstOrDefault();
						if (frc?.CallId is not null)
							matchedIds.Add(frc.CallId);
					}

					List<FunctionCallContent> toRemove = msg.Items.OfType<FunctionCallContent>()
						.Where(fcc => fcc.Id is null || !matchedIds.Contains(fcc.Id))
						.ToList();
					foreach (FunctionCallContent item in toRemove) msg.Items.Remove(item);
				}
			}
		}
	}
}
