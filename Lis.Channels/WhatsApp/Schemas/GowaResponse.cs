using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class GowaResponse<T> {
	[JsonPropertyName("code")]
	public int Code { get; init; }

	[JsonPropertyName("message")]
	public string? Message { get; init; }

	[JsonPropertyName("results")]
	public T? Results { get; init; }
}

public sealed class SendResult {
	[JsonPropertyName("message_id")]
	public string? MessageId { get; init; }

	[JsonPropertyName("status")]
	public string? Status { get; init; }
}
