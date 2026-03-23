namespace Lis.Tests.Harness;

/// <summary>
/// Fluent assertion extensions for <see cref="HarnessResult"/>.
/// Each method returns the result for chaining and throws on failure.
/// </summary>
public static class HarnessAssertions
{
	/// <summary>Assert that the specified tool was called at least once.</summary>
	public static HarnessResult ShouldCallTool(this HarnessResult result, string pluginName, string functionName)
	{
		bool found = result.ToolCalls.Any(tc =>
			tc.PluginName == pluginName && tc.FunctionName == functionName);

		Assert.True(found,
			$"Expected tool '{pluginName}.{functionName}' to be called, but it was not. " +
			$"Actual tool calls: [{FormatToolCalls(result.ToolCalls)}]");

		return result;
	}

	/// <summary>Assert that the specified tool was called with a specific argument containing a value.</summary>
	public static HarnessResult ShouldCallToolWithArg(
		this HarnessResult result, string pluginName, string functionName,
		string argName, string containing)
	{
		HarnessToolCall? match = result.ToolCalls.FirstOrDefault(tc =>
			tc.PluginName == pluginName && tc.FunctionName == functionName
			&& tc.Arguments.TryGetValue(argName, out string? value)
			&& value.Contains(containing, StringComparison.OrdinalIgnoreCase));

		Assert.NotNull(match);

		return result;
	}

	/// <summary>Assert that the specified tool was NOT called.</summary>
	public static HarnessResult ShouldNotCallTool(this HarnessResult result, string pluginName, string functionName)
	{
		bool found = result.ToolCalls.Any(tc =>
			tc.PluginName == pluginName && tc.FunctionName == functionName);

		Assert.False(found,
			$"Expected tool '{pluginName}.{functionName}' to NOT be called, but it was.");

		return result;
	}

	/// <summary>Assert that no tools were called at all.</summary>
	public static HarnessResult ShouldNotCallAnyTools(this HarnessResult result)
	{
		Assert.Empty(result.ToolCalls);
		return result;
	}

	/// <summary>Assert the response is within a token budget.</summary>
	public static HarnessResult ShouldRespondWithin(this HarnessResult result, int maxTokens)
	{
		Assert.True(result.OutputTokens <= maxTokens,
			$"Expected response within {maxTokens} tokens, but got {result.OutputTokens}.");

		return result;
	}

	/// <summary>Assert the response contains the given keyword (case-insensitive).</summary>
	public static HarnessResult ShouldContain(this HarnessResult result, string keyword)
	{
		Assert.Contains(keyword, result.Response, StringComparison.OrdinalIgnoreCase);
		return result;
	}

	/// <summary>Assert the response matches a regex pattern.</summary>
	public static HarnessResult ShouldMatch(this HarnessResult result, string pattern)
	{
		Assert.Matches(pattern, result.Response);
		return result;
	}

	/// <summary>Assert the response does NOT contain the given keyword (case-insensitive).</summary>
	public static HarnessResult ShouldNotContain(this HarnessResult result, string keyword)
	{
		Assert.DoesNotContain(keyword, result.Response, StringComparison.OrdinalIgnoreCase);
		return result;
	}

	/// <summary>Assert the response is not empty or whitespace.</summary>
	public static HarnessResult ResponseShouldNotBeEmpty(this HarnessResult result)
	{
		Assert.False(string.IsNullOrWhiteSpace(result.Response),
			"Expected a non-empty response, but the response was empty or whitespace.");

		return result;
	}

	/// <summary>Assert the exact number of tool calls made.</summary>
	public static HarnessResult ShouldHaveToolCallCount(this HarnessResult result, int expectedCount)
	{
		Assert.Equal(expectedCount, result.ToolCalls.Count);
		return result;
	}

	private static string FormatToolCalls(List<HarnessToolCall> toolCalls) =>
		toolCalls.Count == 0
			? "(none)"
			: string.Join(", ", toolCalls.Select(tc => $"{tc.PluginName}.{tc.FunctionName}"));
}
