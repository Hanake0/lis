using System.Runtime.CompilerServices;
using System.Text;

using Lis.Core.Util;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ToolRunner(ILogger<ToolRunner> logger) {
	private static int MaxIterations =>
		int.TryParse(Environment.GetEnvironmentVariable("LIS_MAX_TOOL_ITERATIONS"), out int v) ? v : 10;

	[Trace("ToolRunner > RunAsync")]
	public async IAsyncEnumerable<ChatMessageContent> RunAsync(
		IChatCompletionService chat, ChatHistory history,
		Kernel kernel, PromptExecutionSettings settings,
		[EnumeratorCancellation] CancellationToken ct = default) {

		for (int i = 0; i < MaxIterations; i++) {
			(ChatMessageContent result, IReadOnlyList<FunctionCallContent> calls) =
				await this.StreamResponseAsync(chat, history, settings, kernel, ct);

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

	[Trace("ToolRunner > StreamResponseAsync")]
	private async Task<(ChatMessageContent, IReadOnlyList<FunctionCallContent>)> StreamResponseAsync(
		IChatCompletionService chat, ChatHistory history,
		PromptExecutionSettings settings, Kernel kernel, CancellationToken ct) {

		AuthorRole?                role       = null;
		StringBuilder              text       = new();
		FunctionCallContentBuilder fccBuilder = new();
		bool                       typingSet  = false;

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
		}

		IReadOnlyList<FunctionCallContent> calls = fccBuilder.Build();

		string? content = text.Length > 0 ? text.ToString() : null;
		ChatMessageContent message = new(role: role ?? AuthorRole.Assistant, content: content);
		foreach (FunctionCallContent call in calls)
			message.Items.Add(call);

		return (message, calls);
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
