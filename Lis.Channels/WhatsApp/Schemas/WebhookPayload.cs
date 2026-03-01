using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class WebhookPayload {
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("chat_jid")]
	public required string ChatJid { get; init; }

	[JsonPropertyName("sender_jid")]
	public required string SenderJid { get; init; }

	[JsonPropertyName("sender_name")]
	public string? SenderName { get; init; }

	[JsonPropertyName("timestamp")]
	public long Timestamp { get; init; }

	[JsonPropertyName("is_from_me")]
	public bool IsFromMe { get; init; }

	[JsonPropertyName("is_group")]
	public bool IsGroup { get; init; }

	[JsonPropertyName("body")]
	public string? Body { get; init; }

	[JsonPropertyName("replied_id")]
	public string? RepliedId { get; init; }

	[JsonPropertyName("quoted_message")]
	public string? QuotedMessage { get; init; }

	[JsonPropertyName("media_type")]
	public string? MediaType { get; init; }

	[JsonPropertyName("media_caption")]
	public string? MediaCaption { get; init; }
}
