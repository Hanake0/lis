using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ToolRunner(ToolAuthRegistry authRegistry, IApprovalService approvalService, IUsageExtractor usageExtractor, IReasoningExtractor reasoningExtractor, ILogger<ToolRunner> logger) {
	internal const string UsageMetadataKey = "LisTokenUsage";

	private static int MaxIterations =>
		int.TryParse(Environment.GetEnvironmentVariable("LIS_MAX_TOOL_ITERATIONS"), out int v) ? v : 10;

	[Trace("ToolRunner > RunAsync")]
	public async IAsyncEnumerable<ChatMessageContent> RunAsync(
		IChatCompletionService chat, ChatHistory history,
		Kernel kernel, PromptExecutionSettings settings,
		IChannelClient? streamSink = null, string? streamChatId = null, string? streamMessageId = null,
		[EnumeratorCancellation] CancellationToken ct = default) {

		for (int i = 0; i < MaxIterations; i++) {
			int countBefore = history.Count;

			(ChatMessageContent result, IReadOnlyList<FunctionCallContent> calls, TokenUsage? usage) =
				await this.StreamResponseAsync(chat, history, settings, kernel, streamSink, streamChatId, streamMessageId, ct);

			// SK's ChatClientChatCompletionService auto-appends a tool-call-only message
			// to ChatHistory when FunctionChoiceBehavior is set (even with autoInvoke: false).
			// Remove it so we can add our own version which includes full text content.
			while (history.Count > countBefore)
				history.RemoveAt(history.Count - 1);

			history.Add(result);
			yield return result;

			if (calls.Count == 0) yield break;

			foreach (FunctionCallContent call in calls) {
				string toolId = $"tool:{Guid.NewGuid():N}";
				string argsJson = call.Arguments is not null ? JsonSerializer.Serialize(call.Arguments) : "{}";
				// Streaming SK FunctionCallContent often has an empty PluginName (the
				// kernel-side lookup hasn't resolved yet) — FunctionName carries the full
				// "plugin_function" form Anthropic returned. Skip the underscore in that case.
				string toolName = string.IsNullOrEmpty(call.PluginName)
					? call.FunctionName ?? "tool"
					: $"{call.PluginName}_{call.FunctionName}";

				if (streamSink is not null && streamChatId is not null)
					await streamSink.SendToolStartAsync(streamChatId, toolId, toolName, argsJson, ct);

				ToolInvocation invocation = await this.InvokeFunctionAsync(kernel, call, ct);
				string resultText = invocation.Result.Result?.ToString() ?? "";

				if (streamSink is not null && streamChatId is not null)
					await streamSink.SendToolResultAsync(streamChatId, toolId, toolName,
						resultText, invocation.IsError ? resultText : null, ct);

				ChatMessageContent toolMsg = invocation.Result.ToChatMessage();
				// Stash the call args + the WS frame id on the persisted message.
				// Args let history recover the parameters the agent used; ToolWsId lets the
				// conversation service push backfilled token counts to the same UI block.
				// IsError lets history restore the failure state so failed calls stay red.
				toolMsg.Metadata = new Dictionary<string, object?> {
					["ToolArgs"] = argsJson,
					["ToolWsId"] = toolId,
					["IsError"]  = invocation.IsError,
				};
				history.Add(toolMsg);
				yield return toolMsg;
			}
		}

		logger.LogWarning("Max tool iterations ({Max}) reached", MaxIterations);
	}

	/// <summary>
	/// Extracts the <see cref="TokenUsage"/> attached by <see cref="RunAsync"/> to an assistant message.
	/// Returns null for tool messages or messages without usage data.
	/// </summary>
	public static TokenUsage? GetUsage(ChatMessageContent message) {
		if (message.Metadata?.TryGetValue(UsageMetadataKey, out object? value) == true)
			return value as TokenUsage;
		return null;
	}

	[Trace("ToolRunner > StreamResponseAsync")]
	private async Task<(ChatMessageContent, IReadOnlyList<FunctionCallContent>, TokenUsage?)> StreamResponseAsync(
		IChatCompletionService chat, ChatHistory history,
		PromptExecutionSettings settings, Kernel kernel,
		IChannelClient? streamSink, string? streamChatId, string? streamMessageId,
		CancellationToken ct) {

		AuthorRole?                role       = null;
		StringBuilder              text       = new();
		FunctionCallContentBuilder fccBuilder = new();
		bool                       typingSet  = false;
		bool                       firstChunk = true;
		IReadOnlyDictionary<string, object?>? lastMetadata = null;

		await foreach (StreamingChatMessageContent chunk in
			chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct)) {

			role ??= chunk.Role;

			// The SK→M.E.AI bridge strips reasoning content from chunk.Items, but the
			// raw provider event lives on chunk.InnerContent. The provider-specific
			// IReasoningExtractor reads the delta from there.
			string? thinkingText = reasoningExtractor.ExtractReasoningDelta(chunk.InnerContent);
			bool isThinking = thinkingText is not null;

			if (isThinking) {
				if (streamSink is not null && streamChatId is not null && !string.IsNullOrEmpty(thinkingText))
					await streamSink.SendThinkingChunkAsync(streamChatId, streamMessageId ?? "", thinkingText, false, ct);
				// Don't append thinking to the text output — it's separate content
				fccBuilder.Append(chunk);
				if (chunk.Metadata is { Count: > 0 }) lastMetadata = chunk.Metadata;
				continue;
			}

			if (!typingSet && chunk.Content is { Length: > 0 }
			    && ToolContext.Channel is not null && ToolContext.ChatId is not null) {
				typingSet = true;
				if (streamSink is null)
					await ToolContext.Channel.SetTypingAsync(ToolContext.ChatId, ct);
			}

			if (chunk.Content is { Length: > 0 }) {
				text.Append(chunk.Content);
				if (streamSink is not null && streamChatId is not null) {
					if (firstChunk) {
						firstChunk = false;
						// End any thinking phase
						await streamSink.SendThinkingChunkAsync(streamChatId, streamMessageId ?? "", "", true, ct);
					}
					await streamSink.SendChunkAsync(streamChatId, streamMessageId ?? "", chunk.Content, false, ct);
				}
			}

			fccBuilder.Append(chunk);

			// Keep the last chunk's metadata — it contains the final usage stats
			if (chunk.Metadata is { Count: > 0 })
				lastMetadata = chunk.Metadata;
		}

		// Signal end of streaming
		if (streamSink is not null && streamChatId is not null && text.Length > 0)
			await streamSink.SendChunkAsync(streamChatId, streamMessageId ?? "", "", true, ct);

		IReadOnlyList<FunctionCallContent> calls = fccBuilder.Build();

		TokenUsage? usage = usageExtractor.Extract(lastMetadata);

		// Build message with usage attached in metadata (thread-safe, per-message)
		Dictionary<string, object?>? metadata = usage is not null
			? new Dictionary<string, object?> { [UsageMetadataKey] = usage }
			: null;

		string? content = text.Length > 0 ? text.ToString() : null;
		ChatMessageContent message = new(role: role ?? AuthorRole.Assistant, content: content, metadata: metadata);
		foreach (FunctionCallContent call in calls)
			message.Items.Add(call);

		return (message, calls, usage);
	}

	[Trace("ToolRunner > InvokeFunctionAsync")]
	private async Task<ToolInvocation> InvokeFunctionAsync(
		Kernel kernel, FunctionCallContent call, CancellationToken ct) {
		try {
			// Authorization gate
			ToolAuthLevel level = authRegistry.GetLevel(call.PluginName, call.FunctionName);

			if (level == ToolAuthLevel.OwnerOnly && !ToolContext.IsOwner) {
				logger.LogWarning("Non-owner tried to invoke owner-only tool '{Tool}'", call.FunctionName);
				return new ToolInvocation(new FunctionResultContent(call, "This tool requires owner authorization."), IsError: true);
			}

			if (level == ToolAuthLevel.ApprovalRequired) {
				// Extract command from call arguments for the approval request
				string? command = call.Arguments?.TryGetValue("command", out object? cmdObj) == true
					? cmdObj?.ToString()
					: null;

				if (command is not null) {
					string? cwd = call.Arguments?.TryGetValue("cwd", out object? cwdObj) == true
						? cwdObj?.ToString()
						: null;

					ApprovalRequest approvalRequest = new(command, cwd, 0, ToolContext.AgentId);
					ApprovalResult  approvalResult  = await approvalService.RequestApprovalAsync(approvalRequest, ct);

					if (approvalResult.Decision is ApprovalDecision.Deny or ApprovalDecision.Timeout) {
						string reason = approvalResult.Decision == ApprovalDecision.Timeout
							? "Approval timed out."
							: "Command execution denied by owner.";
						return new ToolInvocation(new FunctionResultContent(call, reason), IsError: true);
					}
				}
			}

			return new ToolInvocation(await call.InvokeAsync(kernel, ct), IsError: false);
		} catch (Exception ex) {
			if (ex is KeyNotFoundException)
				logger.LogWarning(ex, "Tried to execute tool '{Tool}' but it does not exist", call.FunctionName);
			else if (ex is ArgumentException or ArgumentNullException
			         || ex.InnerException is ArgumentException or ArgumentNullException)
				logger.LogWarning(ex, "Tried to execute tool '{Tool}' without the needed arguments", call.FunctionName);
			else
				logger.LogError(ex, "Error executing tool '{Tool}'", call.FunctionName);

			return new ToolInvocation(new FunctionResultContent(call, ex.Message), IsError: true);
		}
	}

	private readonly record struct ToolInvocation(FunctionResultContent Result, bool IsError);
}
