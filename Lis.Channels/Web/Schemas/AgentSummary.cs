using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

public sealed class AgentSummary {
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("display_name")]
	public string? DisplayName { get; init; }

	[JsonPropertyName("model")]
	public required string Model { get; init; }

	[JsonPropertyName("effort")]
	public string? Effort { get; init; }

	[JsonPropertyName("tool_profile")]
	public string? ToolProfile { get; init; }

	[JsonPropertyName("is_default")]
	public bool IsDefault { get; init; }
}
