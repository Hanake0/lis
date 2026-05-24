using System.Text.Json.Nodes;

namespace Lis.Core.Channel;

/// <summary>
/// Per-request thinking config flowed via AsyncLocal so a DelegatingHandler in the
/// provider can inject the <c>thinking</c> field into the HTTP request body without
/// the agent layer needing a provider reference.
///
/// Set <see cref="Pending"/>.Value to a <see cref="JsonNode"/> (object) shaped per the
/// Anthropic API just before calling the LLM. The provider's HTTP handler reads and
/// clears it. Null means "no thinking" — the handler is a no-op.
/// </summary>
public static class ThinkingContext {
	public static readonly AsyncLocal<JsonNode?> Pending = new();
}
