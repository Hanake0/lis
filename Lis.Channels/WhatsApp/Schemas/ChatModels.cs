using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class ChatInfo {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("unread_count")]
	public int UnreadCount { get; init; }

	[JsonPropertyName("last_message_timestamp")]
	public long LastMessageTimestamp { get; init; }

	[JsonPropertyName("is_group")]
	public bool IsGroup { get; init; }
}

public sealed class ChatMessage {
	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("chat_jid")]
	public string? ChatJid { get; init; }

	[JsonPropertyName("sender_jid")]
	public string? SenderJid { get; init; }

	[JsonPropertyName("sender_name")]
	public string? SenderName { get; init; }

	[JsonPropertyName("body")]
	public string? Body { get; init; }

	[JsonPropertyName("timestamp")]
	public long Timestamp { get; init; }

	[JsonPropertyName("is_from_me")]
	public bool IsFromMe { get; init; }

	[JsonPropertyName("is_group")]
	public bool IsGroup { get; init; }

	[JsonPropertyName("media_type")]
	public string? MediaType { get; init; }
}
