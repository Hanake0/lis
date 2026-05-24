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
}
