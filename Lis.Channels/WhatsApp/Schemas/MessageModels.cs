using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class MediaDownloadResult {
	[JsonPropertyName("mime_type")]
	public string? MimeType { get; init; }

	[JsonPropertyName("file_size")]
	public long FileSize { get; init; }

	[JsonPropertyName("file_name")]
	public string? FileName { get; init; }

	[JsonPropertyName("data")]
	public string? Data { get; init; }
}
