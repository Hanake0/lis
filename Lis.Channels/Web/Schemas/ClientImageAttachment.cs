using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

public sealed class ClientImageAttachment {
	[JsonPropertyName("filename")]
	public string? Filename { get; init; }

	[JsonPropertyName("mime_type")]
	public string? MimeType { get; init; }

	[JsonPropertyName("data_url")]
	public string? DataUrl { get; init; }
}
