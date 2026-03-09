using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class MediaDownloadResult {
	[JsonPropertyName("media_type")]
	public string? MediaType { get; init; }

	[JsonPropertyName("file_path")]
	public string? FilePath { get; init; }

	[JsonPropertyName("filename")]
	public string? Filename { get; init; }

	[JsonPropertyName("file_size")]
	public long FileSize { get; init; }
}
