using Lis.Core.Channel;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed class WhatsAppClient(GowaClient gowa) :IChannelClient {

	[Trace("WhatsAppClient > SendMessageAsync")]
	public async Task<string?> SendMessageAsync(
		string chatId, string message, string? replyToId = null, CancellationToken ct = default) {
		Schemas.SendResult? result = await gowa.SendMessageAsync(
			StripJidSuffix(chatId), message, replyToId, ct: ct);
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

	private static string StripJidSuffix(string jid) {
		int atIndex = jid.IndexOf('@');
		return atIndex > 0 ? jid[..atIndex] : jid;
	}
}
