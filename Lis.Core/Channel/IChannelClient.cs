namespace Lis.Core.Channel;

public interface IChannelClient {
	Task<string?> SendMessageAsync(string chatId, string message, string? replyToId = null, CancellationToken ct = default);
	Task SetTypingAsync(string chatId, CancellationToken ct = default);
	Task MarkReadAsync(string messageId, string chatId, CancellationToken ct = default);
	Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default);
	Task<MediaDownload?> DownloadMediaAsync(string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default);
}
