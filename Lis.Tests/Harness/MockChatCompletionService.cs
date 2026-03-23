using System.Runtime.CompilerServices;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Harness;

/// <summary>
/// A configurable mock <see cref="IChatCompletionService"/> that queues pre-defined responses
/// and records tool calls for assertion. Supports multi-turn conversations with tool call simulation.
/// </summary>
public sealed class MockChatCompletionService : IChatCompletionService
{
	private readonly Queue<MockResponse> _responses = new();
	private readonly List<HarnessToolCall> _recordedToolCalls = [];

	/// <summary>Tool calls captured during the conversation.</summary>
	public IReadOnlyList<HarnessToolCall> RecordedToolCalls => this._recordedToolCalls;

	public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

	/// <summary>
	/// Enqueue a plain text response.
	/// </summary>
	public MockChatCompletionService QueueTextResponse(string text)
	{
		this._responses.Enqueue(new MockResponse { Text = text });
		return this;
	}

	/// <summary>
	/// Enqueue a response that includes tool calls followed by a text response.
	/// The harness will invoke the tool calls, then the next GetChatMessageContentsAsync
	/// call should return the follow-up text.
	/// </summary>
	public MockChatCompletionService QueueToolCallResponse(
		string pluginName, string functionName, Dictionary<string, string>? arguments = null)
	{
		this._responses.Enqueue(new MockResponse
		{
			ToolCalls = [new MockToolCall(pluginName, functionName, arguments ?? [])]
		});
		return this;
	}

	/// <summary>
	/// Enqueue a response with multiple tool calls.
	/// </summary>
	public MockChatCompletionService QueueMultiToolCallResponse(
		params MockToolCall[] toolCalls)
	{
		this._responses.Enqueue(new MockResponse { ToolCalls = [.. toolCalls] });
		return this;
	}

	public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
		ChatHistory chatHistory,
		PromptExecutionSettings? executionSettings = null,
		Kernel? kernel = null,
		CancellationToken cancellationToken = default)
	{
		if (this._responses.Count == 0)
			throw new InvalidOperationException("No more queued responses in MockChatCompletionService.");

		MockResponse response = this._responses.Dequeue();
		List<ChatMessageContent> results = [];

		if (response.ToolCalls.Count > 0)
		{
			ChatMessageContent toolCallMessage = new(AuthorRole.Assistant, content: null);
			foreach (MockToolCall tc in response.ToolCalls)
			{
				this._recordedToolCalls.Add(new HarnessToolCall(
					tc.PluginName, tc.FunctionName, tc.Arguments));

				KernelArguments args = [];
				foreach (KeyValuePair<string, string> kvp in tc.Arguments)
					args[kvp.Key] = kvp.Value;

				toolCallMessage.Items.Add(new FunctionCallContent(
					functionName: tc.FunctionName,
					pluginName: tc.PluginName,
					id: Guid.NewGuid().ToString("N"),
					arguments: args));
			}
			results.Add(toolCallMessage);
		}
		else
		{
			results.Add(new ChatMessageContent(AuthorRole.Assistant, response.Text ?? string.Empty));
		}

		return Task.FromResult<IReadOnlyList<ChatMessageContent>>(results);
	}

	public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
		ChatHistory chatHistory,
		PromptExecutionSettings? executionSettings = null,
		Kernel? kernel = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		IReadOnlyList<ChatMessageContent> messages =
			await this.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

		foreach (ChatMessageContent msg in messages)
		{
			yield return new StreamingChatMessageContent(msg.Role, msg.Content);
		}
	}

	private sealed class MockResponse
	{
		public string? Text { get; init; }
		public List<MockToolCall> ToolCalls { get; init; } = [];
	}
}

/// <summary>
/// Represents a tool call to be simulated by the mock.
/// </summary>
public sealed record MockToolCall(
	string PluginName,
	string FunctionName,
	Dictionary<string, string> Arguments);
