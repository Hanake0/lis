using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class WebhookEnvelope {
	[JsonPropertyName("event")]
	public string? Event { get; init; }

	[JsonPropertyName("device_id")]
	public string? DeviceId { get; init; }

	[JsonPropertyName("payload")]
	public WebhookPayload? Payload { get; init; }
}

public sealed class WebhookPayload {
	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("chat_id")]
	public string? ChatId { get; init; }

	[JsonPropertyName("from")]
	public string? From { get; init; }

	[JsonPropertyName("from_name")]
	public string? FromName { get; init; }

	[JsonPropertyName("timestamp")]
	public string? Timestamp { get; init; }

	[JsonPropertyName("is_from_me")]
	public bool IsFromMe { get; init; }

	[JsonPropertyName("body")]
	public string? Body { get; init; }

	[JsonPropertyName("quoted_message")]
	public string? QuotedMessage { get; init; }

	[JsonPropertyName("media_type")]
	public string? MediaType { get; init; }

	[JsonPropertyName("media_caption")]
	public string? MediaCaption { get; init; }
}
