using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class SendPollRequest {
	[JsonPropertyName("phone")]
	public required string Phone { get; init; }

	[JsonPropertyName("question")]
	public required string Question { get; init; }

	[JsonPropertyName("options")]
	public required string[] Options { get; init; }

	[JsonPropertyName("max_answer")]
	public int MaxAnswer { get; init; }

	[JsonPropertyName("duration")]
	public int? Duration { get; init; }
}
