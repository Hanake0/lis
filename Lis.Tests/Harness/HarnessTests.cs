using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Harness;

/// <summary>
/// Tests for <see cref="MockChatCompletionService"/> and <see cref="LlmTestHarness"/> internals.
/// </summary>
public class HarnessTests
{
	// ── MockChatCompletionService ────────────────────────────────

	[Fact]
	public async Task Mock_TextResponse_ReturnsContent()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Hello!");

		IReadOnlyList<ChatMessageContent> results =
			await mock.GetChatMessageContentsAsync([]);

		Assert.Single(results);
		Assert.Equal(AuthorRole.Assistant, results[0].Role);
		Assert.Equal("Hello!", results[0].Content);
	}

	[Fact]
	public async Task Mock_ToolCallResponse_ReturnsFunctionCallContent()
	{
		MockChatCompletionService mock = new();
		mock.QueueToolCallResponse("mem", "create_memory", new() { ["content"] = "test" });

		IReadOnlyList<ChatMessageContent> results =
			await mock.GetChatMessageContentsAsync([]);

		Assert.Single(results);
		FunctionCallContent? call = results[0].Items.OfType<FunctionCallContent>().FirstOrDefault();
		Assert.NotNull(call);
		Assert.Equal("mem", call.PluginName);
		Assert.Equal("create_memory", call.FunctionName);
	}

	[Fact]
	public async Task Mock_MultipleQueued_ReturnsInOrder()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("First");
		mock.QueueTextResponse("Second");

		IReadOnlyList<ChatMessageContent> first = await mock.GetChatMessageContentsAsync([]);
		IReadOnlyList<ChatMessageContent> second = await mock.GetChatMessageContentsAsync([]);

		Assert.Equal("First", first[0].Content);
		Assert.Equal("Second", second[0].Content);
	}

	[Fact]
	public async Task Mock_EmptyQueue_Throws()
	{
		MockChatCompletionService mock = new();

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			mock.GetChatMessageContentsAsync([]));
	}

	[Fact]
	public async Task Mock_RecordsToolCalls()
	{
		MockChatCompletionService mock = new();
		mock.QueueToolCallResponse("dt", "get_current_datetime");

		await mock.GetChatMessageContentsAsync([]);

		Assert.Single(mock.RecordedToolCalls);
		Assert.Equal("dt", mock.RecordedToolCalls[0].PluginName);
		Assert.Equal("get_current_datetime", mock.RecordedToolCalls[0].FunctionName);
	}

	[Fact]
	public async Task Mock_MultiToolCallResponse_AllRecorded()
	{
		MockChatCompletionService mock = new();
		mock.QueueMultiToolCallResponse(
			new MockToolCall("dt", "get_current_datetime", []),
			new MockToolCall("mem", "search_memories", new() { ["query"] = "test" })
		);

		await mock.GetChatMessageContentsAsync([]);

		Assert.Equal(2, mock.RecordedToolCalls.Count);
	}

	[Fact]
	public async Task Mock_StreamingResponse_YieldsContent()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Streamed");

		List<StreamingChatMessageContent> chunks = [];
		await foreach (StreamingChatMessageContent chunk in mock.GetStreamingChatMessageContentsAsync([]))
			chunks.Add(chunk);

		Assert.Single(chunks);
		Assert.Equal("Streamed", chunks[0].Content);
	}

	// ── LlmTestHarness ─────────────────────────────────────────

	[Fact]
	public async Task Harness_SimpleMessage_CapturesResponse()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Hi there!");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Hello");

		Assert.Equal("Hi there!", result.Response);
		Assert.Empty(result.ToolCalls);
		Assert.True(result.OutputTokens > 0);
		Assert.True(result.Duration > TimeSpan.Zero);
	}

	[Fact]
	public async Task Harness_ToolCall_CapturedAndRecorded()
	{
		MockChatCompletionService mock = new();
		mock.QueueToolCallResponse("mem", "create_memory", new() { ["content"] = "test data" });
		mock.QueueTextResponse("Saved!");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Remember this");

		Assert.Single(result.ToolCalls);
		Assert.Equal("mem", result.ToolCalls[0].PluginName);
		Assert.Equal("create_memory", result.ToolCalls[0].FunctionName);
		Assert.Equal("test data", result.ToolCalls[0].Arguments["content"]);
		Assert.Equal("Saved!", result.Response);
	}

	[Fact]
	public async Task Harness_MultipleToolCalls_AllCaptured()
	{
		MockChatCompletionService mock = new();
		mock.QueueMultiToolCallResponse(
			new MockToolCall("dt", "get_current_datetime", []),
			new MockToolCall("mem", "search_memories", new() { ["query"] = "test" })
		);
		mock.QueueTextResponse("Done.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Search");

		Assert.Equal(2, result.ToolCalls.Count);
		Assert.Equal("Done.", result.Response);
	}

	[Fact]
	public async Task Harness_HistoryContainsAllMessages()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Reply");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Test");

		// system + user + assistant = 3
		Assert.Equal(3, result.History.Count);
		Assert.Equal(AuthorRole.System, result.History[0].Role);
		Assert.Equal(AuthorRole.User, result.History[1].Role);
		Assert.Equal(AuthorRole.Assistant, result.History[2].Role);
	}

	[Fact]
	public async Task Harness_CustomSystemPrompt_Applied()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Response");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Hello",
			opts => opts.SystemPrompt = "Custom prompt");

		Assert.Equal("Custom prompt", result.History[0].Content);
	}

	[Fact]
	public async Task Harness_TokenEstimation_Works()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("This is a test response with several words.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Test");

		Assert.True(result.OutputTokens > 0);
	}

	[Fact]
	public async Task Harness_EmptyResponse_ZeroTokens()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Test");

		Assert.Equal(0, result.OutputTokens);
		Assert.Equal(string.Empty, result.Response);
	}

	[Fact]
	public async Task Harness_MaxIterations_Respected()
	{
		MockChatCompletionService mock = new();
		// Queue more tool calls than max iterations
		mock.QueueToolCallResponse("dt", "get_current_datetime");
		mock.QueueToolCallResponse("dt", "get_current_datetime");
		mock.QueueToolCallResponse("dt", "get_current_datetime");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Test",
			opts => opts.MaxIterations = 2);

		// Should stop after 2 iterations even though there are more responses
		Assert.True(result.ToolCalls.Count <= 2);
	}
}
