using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Channel;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed class WhatsAppClient(GowaClient gowa, IMessageFormatter formatter) : IChannelClient {

	[Trace("WhatsAppClient > SendMessageAsync")]
	public async Task<string?> SendMessageAsync(
		string chatId, string message, string? replyToId = null, CancellationToken ct = default) {
		string formatted = formatter.Format(message);
		SendResult? result = await gowa.SendMessageAsync(
			StripJidSuffix(chatId), formatted, replyToId, ct: ct);
		return result?.MessageId;
	}

	[Trace("WhatsAppClient > SetTypingAsync")]
	public async Task SetTypingAsync(string chatId, CancellationToken ct = default) {
		await gowa.SendChatPresenceAsync(StripJidSuffix(chatId), "start", ct);
	}

	[Trace("WhatsAppClient > MarkReadAsync")]
	public async Task MarkReadAsync(string messageId, string chatId, CancellationToken ct = default) {
		await gowa.MarkMessageReadAsync(messageId, StripJidSuffix(chatId), ct);
	}

	[Trace("WhatsAppClient > ReactAsync")]
	public async Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default) {
		await gowa.ReactToMessageAsync(messageId, StripJidSuffix(chatId), emoji, ct);
	}

	[Trace("WhatsAppClient > DownloadMediaAsync")]
	public async Task<MediaDownload?> DownloadMediaAsync(
		string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default) {

		string path;
		if (mediaPath is not null) {
			path = mediaPath;
		} else {
			// Fallback: call download endpoint (re-downloads from WA — slower)
			MediaDownloadResult? result = await gowa.DownloadMediaAsync(
				messageId, StripJidSuffix(chatId), ct);
			if (result?.FilePath is null) return null;
			path = result.FilePath;
		}

		byte[] data = await gowa.FetchFileAsync(path, ct);
		if (data.Length == 0) return null;

		string mimeType = MimeFromExtension(Path.GetExtension(path));
		return new MediaDownload(data, mimeType);
	}

	private static string StripJidSuffix(string jid) {
		int atIndex = jid.IndexOf('@');
		return atIndex > 0 ? jid[..atIndex] : jid;
	}

	private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch {
		".jpg" or ".jpeg" => "image/jpeg",
		".png"            => "image/png",
		".webp"           => "image/webp",
		".gif"            => "image/gif",
		".ogg"            => "audio/ogg",
		".mp3"            => "audio/mpeg",
		".m4a"            => "audio/mp4",
		".wav"            => "audio/wav",
		".mp4"            => "video/mp4",
		".pdf"            => "application/pdf",
		_                 => "application/octet-stream"
	};
}
