namespace Lis.Core.Channel;

public interface IChannelClient {
	Task<string?> SendMessageAsync(string chatId, string message, string? replyToId = null, CancellationToken ct = default);
	Task SetTypingAsync(string chatId, CancellationToken ct = default);
	Task StopTypingAsync(string chatId, CancellationToken ct = default);
	Task MarkReadAsync(string messageId, string chatId, CancellationToken ct = default);
	Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default);
	Task<MediaDownload?> DownloadMediaAsync(string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default);

	// Streaming — default no-ops for channels that don't support it (e.g. WhatsApp).
	Task SendChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default)
		=> Task.CompletedTask;
	Task SendThinkingChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default)
		=> Task.CompletedTask;
	Task SendToolStartAsync(string chatId, string messageId, string toolName, string args, CancellationToken ct = default)
		=> Task.CompletedTask;
	Task SendToolResultAsync(string chatId, string messageId, string toolName, string result, string? error, CancellationToken ct = default)
		=> Task.CompletedTask;

	/// <summary>
	/// Surfaces a backend error to the user in the chat UI. Channels without a dedicated
	/// error frame (e.g. WhatsApp) can override this to send a regular text message.
	/// </summary>
	Task SendErrorAsync(string chatId, string error, CancellationToken ct = default)
		=> Task.CompletedTask;
}
