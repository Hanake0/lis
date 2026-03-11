using System.Text.Json;
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

/// <summary>
/// Gowa webhook payload. Media is sent as nested objects (e.g. "image", "audio", "sticker")
/// rather than flat fields. JsonExtensionData captures these and computed
/// properties derive <see cref="MediaType"/>, <see cref="MediaPath"/>, and <see cref="MediaCaption"/>.
/// </summary>
public sealed class WebhookPayload {
	private static readonly string[] MediaKeys = ["image", "audio", "sticker", "video", "document"];

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

	[JsonPropertyName("replied_to_id")]
	public string? RepliedToId { get; init; }

	[JsonPropertyName("quoted_body")]
	public string? QuotedBody { get; init; }

	[JsonPropertyName("from_lid")]
	public string? FromLid { get; init; }

	[JsonPropertyName("chat_lid")]
	public string? ChatLid { get; init; }

	[JsonPropertyName("forwarded")]
	public bool Forwarded { get; init; }

	[JsonPropertyName("view_once")]
	public bool ViewOnce { get; init; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extensions { get; set; }

	/// <summary>Derived from which nested media field is present.</summary>
	public string? MediaType {
		get {
			if (this.Extensions is null) return null;
			foreach (string key in MediaKeys)
				if (this.Extensions.ContainsKey(key))
					return key;
			return null;
		}
	}

	/// <summary>
	/// File path from auto-download mode (string or {"path":"..."}).
	/// Null when auto-download is OFF (webhook has {"url":"..."} instead).
	/// </summary>
	public string? MediaPath => this.GetMediaField("path") ?? this.GetMediaString();

	/// <summary>Caption from {"path":"...", "caption":"..."} or {"url":"...", "caption":"..."}.</summary>
	public string? MediaCaption => this.GetMediaField("caption");

	private string? GetMediaString() {
		if (this.MediaType is null || this.Extensions?.TryGetValue(this.MediaType, out JsonElement el) != true) return null;
		return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
	}

	private string? GetMediaField(string field) {
		if (this.MediaType is null || this.Extensions?.TryGetValue(this.MediaType, out JsonElement el) != true) return null;
		if (el.ValueKind != JsonValueKind.Object) return null;
		return el.TryGetProperty(field, out JsonElement prop) ? prop.GetString() : null;
	}
}
