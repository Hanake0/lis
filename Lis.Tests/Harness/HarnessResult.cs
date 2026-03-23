using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Harness;

/// <summary>
/// Captures the full outcome of a simulated AI conversation turn.
/// </summary>
public sealed class HarnessResult
{
	/// <summary>Final assistant text response.</summary>
	public string Response { get; init; } = string.Empty;

	/// <summary>Every tool invocation requested by the assistant.</summary>
	public List<HarnessToolCall> ToolCalls { get; init; } = [];

	/// <summary>Estimated output token count (BPE o200k_base).</summary>
	public int OutputTokens { get; init; }

	/// <summary>Wall-clock duration of the simulated turn.</summary>
	public TimeSpan Duration { get; init; }

	/// <summary>Full chat history including system, user, assistant, and tool messages.</summary>
	public ChatHistory History { get; init; } = [];
}

/// <summary>
/// Represents a single tool invocation captured during the harness run.
/// </summary>
public sealed record HarnessToolCall(
	string PluginName,
	string FunctionName,
	Dictionary<string, string> Arguments);
