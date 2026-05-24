using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

public sealed class ChatSummary {
	[JsonPropertyName("chat_id")]
	public required string ChatId { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; init; }

	[JsonPropertyName("archived_at")]
	public DateTimeOffset? ArchivedAt { get; init; }
}
