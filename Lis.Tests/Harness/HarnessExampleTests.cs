namespace Lis.Tests.Harness;

/// <summary>
/// Example tests demonstrating the LLM test harness API.
/// These show how to write tests for AI-powered features.
/// </summary>
public class HarnessExampleTests
{
	// ── Greeting: should NOT trigger any tools ──────────────────

	[Fact]
	public async Task Greeting_DoesNotTriggerTools()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Hello! How can I help you today?");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Hi there!");

		result
			.ShouldNotCallAnyTools()
			.ShouldContain("Hello")
			.ResponseShouldNotBeEmpty();
	}

	// ── Memory creation: should call mem.create_memory ──────────

	[Fact]
	public async Task RememberBirthday_TriggersMemoryTool()
	{
		MockChatCompletionService mock = new();
		mock.QueueToolCallResponse("mem", "create_memory", new()
		{
			["content"] = "User's birthday is January 1st"
		});
		mock.QueueTextResponse("Got it! I'll remember your birthday is January 1st.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Remember my birthday is January 1st");

		result
			.ShouldCallTool("mem", "create_memory")
			.ShouldCallToolWithArg("mem", "create_memory", "content", "birthday")
			.ShouldNotCallTool("exec", "run_command")
			.ShouldContain("remember")
			.ShouldHaveToolCallCount(1);
	}

	// ── Token budget: response should stay under limit ──────────

	[Fact]
	public async Task Response_StaysUnderTokenBudget()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Short answer.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("What time is it?");

		result.ShouldRespondWithin(maxTokens: 500);
	}

	// ── Snapshot workflow example ────────────────────────────────

	[Fact]
	public async Task Snapshot_SaveAndCompare()
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"lis_example_{Guid.NewGuid():N}");
		try
		{
			MockChatCompletionService mock = new();
			mock.QueueTextResponse("The current date is 2025-01-15.");

			LlmTestHarness harness = new(mock);
			HarnessResult result = await harness.SimulateMessageAsync("What's the date?");

			SnapshotManager snapshots = new(tempDir);

			// First run: creates new snapshot
			SnapshotComparison firstRun = snapshots.CompareWithSnapshot("date_query", result);
			Assert.False(firstRun.SnapshotExists);

			// Approve it
			snapshots.ApproveSnapshot("date_query");

			// Second run with same output: matches
			SnapshotComparison secondRun = snapshots.CompareWithSnapshot("date_query", result);
			Assert.True(secondRun.IsMatch);
		}
		finally
		{
			if (Directory.Exists(tempDir))
				Directory.Delete(tempDir, recursive: true);
		}
	}

	// ── Multi-tool call ─────────────────────────────────────────

	[Fact]
	public async Task MultiToolCall_BothToolsCaptured()
	{
		MockChatCompletionService mock = new();
		mock.QueueMultiToolCallResponse(
			new MockToolCall("dt", "get_current_datetime", []),
			new MockToolCall("mem", "search_memories", new() { ["query"] = "meeting" })
		);
		mock.QueueTextResponse("Your next meeting is at 3 PM.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("When is my next meeting?");

		result
			.ShouldCallTool("dt", "get_current_datetime")
			.ShouldCallTool("mem", "search_memories")
			.ShouldHaveToolCallCount(2)
			.ShouldContain("meeting");
	}

	// ── Custom system prompt ────────────────────────────────────

	[Fact]
	public async Task CustomSystemPrompt_IncludedInHistory()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("I am Lis, your AI assistant.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Who are you?",
			opts => opts.SystemPrompt = "You are Lis, a personal AI assistant.");

		Assert.True(result.History.Count >= 3); // system + user + assistant
		Assert.Equal("You are Lis, a personal AI assistant.", result.History[0].Content);
	}

	// ── Regex matching ──────────────────────────────────────────

	[Fact]
	public async Task Response_MatchesExpectedPattern()
	{
		MockChatCompletionService mock = new();
		mock.QueueTextResponse("Memory #42 saved successfully.");

		LlmTestHarness harness = new(mock);
		HarnessResult result = await harness.SimulateMessageAsync("Save this note");

		result.ShouldMatch(@"Memory #\d+ saved");
	}
}
