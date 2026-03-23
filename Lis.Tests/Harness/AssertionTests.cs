namespace Lis.Tests.Harness;

public class AssertionTests
{
	private static HarnessResult MakeResult(
		string response = "Hello world",
		List<HarnessToolCall>? toolCalls = null,
		int outputTokens = 10) => new()
	{
		Response = response,
		ToolCalls = toolCalls ?? [],
		OutputTokens = outputTokens,
		Duration = TimeSpan.FromMilliseconds(100),
		History = []
	};

	// ── ShouldCallTool ──────────────────────────────────────────

	[Fact]
	public void ShouldCallTool_ToolPresent_Passes()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("mem", "create_memory", new() { ["content"] = "birthday" })
		]);

		result.ShouldCallTool("mem", "create_memory");
	}

	[Fact]
	public void ShouldCallTool_ToolAbsent_Throws()
	{
		HarnessResult result = MakeResult();

		Assert.ThrowsAny<Exception>(() => result.ShouldCallTool("mem", "create_memory"));
	}

	[Fact]
	public void ShouldCallTool_WrongPlugin_Throws()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("dt", "get_current_datetime", [])
		]);

		Assert.ThrowsAny<Exception>(() => result.ShouldCallTool("mem", "create_memory"));
	}

	// ── ShouldCallToolWithArg ───────────────────────────────────

	[Fact]
	public void ShouldCallToolWithArg_ArgContainsValue_Passes()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("mem", "create_memory", new() { ["content"] = "my birthday is Jan 1" })
		]);

		result.ShouldCallToolWithArg("mem", "create_memory", "content", "birthday");
	}

	[Fact]
	public void ShouldCallToolWithArg_ArgMissing_Throws()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("mem", "create_memory", new() { ["content"] = "hello" })
		]);

		Assert.ThrowsAny<Exception>(() =>
			result.ShouldCallToolWithArg("mem", "create_memory", "content", "birthday"));
	}

	[Fact]
	public void ShouldCallToolWithArg_CaseInsensitive()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("mem", "create_memory", new() { ["content"] = "My BIRTHDAY" })
		]);

		result.ShouldCallToolWithArg("mem", "create_memory", "content", "birthday");
	}

	// ── ShouldNotCallTool ───────────────────────────────────────

	[Fact]
	public void ShouldNotCallTool_ToolAbsent_Passes()
	{
		HarnessResult result = MakeResult();

		result.ShouldNotCallTool("exec", "run_command");
	}

	[Fact]
	public void ShouldNotCallTool_ToolPresent_Throws()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("exec", "run_command", new() { ["command"] = "rm -rf /" })
		]);

		Assert.ThrowsAny<Exception>(() => result.ShouldNotCallTool("exec", "run_command"));
	}

	// ── ShouldNotCallAnyTools ───────────────────────────────────

	[Fact]
	public void ShouldNotCallAnyTools_NoTools_Passes()
	{
		HarnessResult result = MakeResult();

		result.ShouldNotCallAnyTools();
	}

	[Fact]
	public void ShouldNotCallAnyTools_HasTools_Throws()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("dt", "get_current_datetime", [])
		]);

		Assert.ThrowsAny<Exception>(() => result.ShouldNotCallAnyTools());
	}

	// ── ShouldRespondWithin ─────────────────────────────────────

	[Fact]
	public void ShouldRespondWithin_UnderBudget_Passes()
	{
		HarnessResult result = MakeResult(outputTokens: 100);

		result.ShouldRespondWithin(maxTokens: 500);
	}

	[Fact]
	public void ShouldRespondWithin_OverBudget_Throws()
	{
		HarnessResult result = MakeResult(outputTokens: 600);

		Assert.ThrowsAny<Exception>(() => result.ShouldRespondWithin(maxTokens: 500));
	}

	[Fact]
	public void ShouldRespondWithin_ExactBudget_Passes()
	{
		HarnessResult result = MakeResult(outputTokens: 500);

		result.ShouldRespondWithin(maxTokens: 500);
	}

	// ── ShouldContain ───────────────────────────────────────────

	[Fact]
	public void ShouldContain_KeywordPresent_Passes()
	{
		HarnessResult result = MakeResult(response: "Hello world, how are you?");

		result.ShouldContain("world");
	}

	[Fact]
	public void ShouldContain_CaseInsensitive()
	{
		HarnessResult result = MakeResult(response: "Hello World");

		result.ShouldContain("hello");
	}

	[Fact]
	public void ShouldContain_KeywordAbsent_Throws()
	{
		HarnessResult result = MakeResult(response: "Goodbye");

		Assert.ThrowsAny<Exception>(() => result.ShouldContain("hello"));
	}

	// ── ShouldMatch ─────────────────────────────────────────────

	[Fact]
	public void ShouldMatch_PatternMatches_Passes()
	{
		HarnessResult result = MakeResult(response: "Memory #42 saved.");

		result.ShouldMatch(@"Memory #\d+ saved\.");
	}

	[Fact]
	public void ShouldMatch_PatternDoesNotMatch_Throws()
	{
		HarnessResult result = MakeResult(response: "No match here");

		Assert.ThrowsAny<Exception>(() => result.ShouldMatch(@"^Memory #\d+$"));
	}

	// ── ShouldNotContain ────────────────────────────────────────

	[Fact]
	public void ShouldNotContain_KeywordAbsent_Passes()
	{
		HarnessResult result = MakeResult(response: "All good");

		result.ShouldNotContain("error");
	}

	[Fact]
	public void ShouldNotContain_KeywordPresent_Throws()
	{
		HarnessResult result = MakeResult(response: "An error occurred");

		Assert.ThrowsAny<Exception>(() => result.ShouldNotContain("error"));
	}

	// ── ResponseShouldNotBeEmpty ────────────────────────────────

	[Fact]
	public void ResponseShouldNotBeEmpty_HasContent_Passes()
	{
		HarnessResult result = MakeResult(response: "Hello");

		result.ResponseShouldNotBeEmpty();
	}

	[Fact]
	public void ResponseShouldNotBeEmpty_Empty_Throws()
	{
		HarnessResult result = MakeResult(response: "");

		Assert.ThrowsAny<Exception>(() => result.ResponseShouldNotBeEmpty());
	}

	[Fact]
	public void ResponseShouldNotBeEmpty_Whitespace_Throws()
	{
		HarnessResult result = MakeResult(response: "   ");

		Assert.ThrowsAny<Exception>(() => result.ResponseShouldNotBeEmpty());
	}

	// ── ShouldHaveToolCallCount ─────────────────────────────────

	[Fact]
	public void ShouldHaveToolCallCount_Correct_Passes()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("mem", "create_memory", []),
			new HarnessToolCall("dt", "get_current_datetime", [])
		]);

		result.ShouldHaveToolCallCount(2);
	}

	[Fact]
	public void ShouldHaveToolCallCount_Wrong_Throws()
	{
		HarnessResult result = MakeResult();

		Assert.ThrowsAny<Exception>(() => result.ShouldHaveToolCallCount(1));
	}

	// ── Chaining ────────────────────────────────────────────────

	[Fact]
	public void Assertions_CanBeChained()
	{
		HarnessResult result = MakeResult(
			response: "Memory #1 saved successfully.",
			toolCalls: [new HarnessToolCall("mem", "create_memory", new() { ["content"] = "birthday" })],
			outputTokens: 20);

		result
			.ShouldCallTool("mem", "create_memory")
			.ShouldCallToolWithArg("mem", "create_memory", "content", "birthday")
			.ShouldNotCallTool("exec", "run_command")
			.ShouldContain("saved")
			.ShouldNotContain("error")
			.ShouldMatch(@"Memory #\d+")
			.ShouldRespondWithin(maxTokens: 100)
			.ResponseShouldNotBeEmpty()
			.ShouldHaveToolCallCount(1);
	}

	// ── Edge cases ──────────────────────────────────────────────

	[Fact]
	public void NullResponseInToolCalls_EmptyArgs()
	{
		HarnessResult result = MakeResult(toolCalls: [
			new HarnessToolCall("test", "func", [])
		]);

		result.ShouldCallTool("test", "func");
		Assert.ThrowsAny<Exception>(() => result.ShouldCallToolWithArg("test", "func", "key", "value"));
	}

	[Fact]
	public void EmptyToolCalls_ShouldNotCallAnyPasses()
	{
		HarnessResult result = MakeResult(toolCalls: []);

		result.ShouldNotCallAnyTools();
		result.ShouldHaveToolCallCount(0);
	}
}
