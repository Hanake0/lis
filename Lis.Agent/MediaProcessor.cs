using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.Extensions.Logging;

namespace Lis.Agent;

public interface IMediaProcessor {
	Task<ProcessedMedia?> ProcessAsync(IncomingMessage message, CancellationToken ct);
}

public sealed record ProcessedMedia(byte[] Data, string MimeType, string? Transcription);

public sealed class MediaProcessor(
	IChannelClient          channelClient,
	ILogger<MediaProcessor> logger,
	ITranscriptionService?  transcriptionService = null) : IMediaProcessor {

	private const int MaxMediaSizeBytes = 10 * 1024 * 1024;

	[Trace("MediaProcessor > ProcessAsync")]
	public async Task<ProcessedMedia?> ProcessAsync(IncomingMessage message, CancellationToken ct) {
		if (message.MediaType is null) return null;

		MediaDownload? download = await channelClient.DownloadMediaAsync(
			message.ExternalId, message.ChatId, message.MediaPath, ct);

		if (download is null) return null;
		if (download.Data.Length == 0) return null;

		if (download.Data.Length > MaxMediaSizeBytes) {
			logger.LogWarning("Media too large ({Size} bytes), skipping {Id}",
				download.Data.Length, message.ExternalId);
			return null;
		}

		string? transcription = message.MediaType is "audio" or "ptt"
			? await this.TranscribeAsync(download.Data, download.MimeType, ct)
			: null;

		return new ProcessedMedia(download.Data, download.MimeType, transcription);
	}

	private async Task<string?> TranscribeAsync(byte[] data, string mimeType, CancellationToken ct) {
		if (transcriptionService is null) return null;
		try {
			return await transcriptionService.TranscribeAsync(data, mimeType, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Audio transcription failed");
			return null;
		}
	}
}
