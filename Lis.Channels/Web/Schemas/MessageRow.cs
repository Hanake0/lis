using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

public sealed class MessageRow {
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("role")]
	public required string Role { get; init; }

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	[JsonPropertyName("timestamp")]
	public DateTimeOffset Timestamp { get; init; }

	[JsonPropertyName("input_tokens")]
	public int? InputTokens { get; init; }

	[JsonPropertyName("output_tokens")]
	public int? OutputTokens { get; init; }

	[JsonPropertyName("cache_read_tokens")]
	public int? CacheReadTokens { get; init; }

	[JsonPropertyName("cache_creation_tokens")]
	public int? CacheCreationTokens { get; init; }

	[JsonPropertyName("thinking_tokens")]
	public int? ThinkingTokens { get; init; }

	[JsonPropertyName("tool_name")]
	public string? ToolName { get; init; }
}
