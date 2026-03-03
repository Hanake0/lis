using System.Runtime.CompilerServices;
using System.Text;

using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ToolRunner(IUsageExtractor usageExtractor, ILogger<ToolRunner> logger) {
	internal const string UsageMetadataKey = "LisTokenUsage";

	private static int MaxIterations =>
		int.TryParse(Environment.GetEnvironmentVariable("LIS_MAX_TOOL_ITERATIONS"), out int v) ? v : 10;

	[Trace("ToolRunner > RunAsync")]
	public async IAsyncEnumerable<ChatMessageContent> RunAsync(
		IChatCompletionService chat, ChatHistory history,
		Kernel kernel, PromptExecutionSettings settings,
		[EnumeratorCancellation] CancellationToken ct = default) {

		for (int i = 0; i < MaxIterations; i++) {
			int countBefore = history.Count;

			(ChatMessageContent result, IReadOnlyList<FunctionCallContent> calls, TokenUsage? usage) =
				await this.StreamResponseAsync(chat, history, settings, kernel, ct);

			// SK's ChatClientChatCompletionService auto-appends a tool-call-only message
			// to ChatHistory when FunctionChoiceBehavior is set (even with autoInvoke: false).
			// Remove it so we can add our own version which includes full text content.
			while (history.Count > countBefore)
				history.RemoveAt(history.Count - 1);

			history.Add(result);
			yield return result;

			if (calls.Count == 0) yield break;

			foreach (FunctionCallContent call in calls) {
				ChatMessageContent toolMsg = (await this.InvokeFunctionAsync(kernel, call, ct)).ToChatMessage();
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
		PromptExecutionSettings settings, Kernel kernel, CancellationToken ct) {

		AuthorRole?                role       = null;
		StringBuilder              text       = new();
		FunctionCallContentBuilder fccBuilder = new();
		bool                       typingSet  = false;
		IReadOnlyDictionary<string, object?>? lastMetadata = null;

		await foreach (StreamingChatMessageContent chunk in
			chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct)) {

			role ??= chunk.Role;

			if (!typingSet && chunk.Content is { Length: > 0 }
			    && ToolContext.Channel is not null && ToolContext.ChatId is not null) {
				typingSet = true;
				await ToolContext.Channel.SetTypingAsync(ToolContext.ChatId, ct);
			}

			if (chunk.Content is not null)
				text.Append(chunk.Content);

			fccBuilder.Append(chunk);

			// Keep the last chunk's metadata — it contains the final usage stats
			if (chunk.Metadata is { Count: > 0 })
				lastMetadata = chunk.Metadata;
		}

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
	private async Task<FunctionResultContent> InvokeFunctionAsync(
		Kernel kernel, FunctionCallContent call, CancellationToken ct) {
		try {
			return await call.InvokeAsync(kernel, ct);
		} catch (Exception ex) {
			if (ex is KeyNotFoundException)
				logger.LogWarning(ex, "Tried to execute tool '{Tool}' but it does not exist", call.FunctionName);
			else if (ex is ArgumentException or ArgumentNullException
			         || ex.InnerException is ArgumentException or ArgumentNullException)
				logger.LogWarning(ex, "Tried to execute tool '{Tool}' without the needed arguments", call.FunctionName);
			else
				logger.LogError(ex, "Error executing tool '{Tool}'", call.FunctionName);

			return new FunctionResultContent(call, ex.Message);
		}
	}
}
