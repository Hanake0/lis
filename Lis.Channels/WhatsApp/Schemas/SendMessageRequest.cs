using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class SendMessageRequest {
	[JsonPropertyName("phone")]
	public required string Phone { get; init; }

	[JsonPropertyName("message")]
	public required string Message { get; init; }

	[JsonPropertyName("reply_message_id")]
	public string? ReplyMessageId { get; init; }

	[JsonPropertyName("is_forwarded")]
	public bool? IsForwarded { get; init; }

	[JsonPropertyName("duration")]
	public int? Duration { get; init; }

	[JsonPropertyName("mentions")]
	public string[]? Mentions { get; init; }
}
