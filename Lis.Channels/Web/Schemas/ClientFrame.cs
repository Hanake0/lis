using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

/// <summary>
/// Client → server WebSocket frame. Flat shape; <see cref="Type"/> is the discriminator.
/// </summary>
public sealed class ClientFrame {
	[JsonPropertyName("type")]
	public string? Type { get; init; }

	[JsonPropertyName("chat_id")]
	public string? ChatId { get; init; }

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	/// <summary>One of: "default", "immediate", "queue_only", "interrupt". Defaults to "immediate" for web.</summary>
	[JsonPropertyName("dispatch")]
	public string? Dispatch { get; init; }

	[JsonPropertyName("limit")]
	public int? Limit { get; init; }

	// Per-message overrides
	[JsonPropertyName("model")]
	public string? Model { get; init; }

	[JsonPropertyName("effort")]
	public string? Effort { get; init; }

	[JsonPropertyName("tool_profile")]
	public string? ToolProfile { get; init; }

	// Agent management
	[JsonPropertyName("agent_name")]
	public string? AgentName { get; init; }
}
