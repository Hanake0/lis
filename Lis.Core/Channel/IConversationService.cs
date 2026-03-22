namespace Lis.Core.Channel;

public interface IConversationService {
	Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct);
	Task HandleTypingAsync(string            chatId,  CancellationToken ct);
	Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct);
	Task HandleReactionAsync(string messageId, string chatId, string emoji, string senderId, CancellationToken ct);

	Task<string?> ResolvePhoneToNameAsync(string chatId, string phone, CancellationToken ct);
	Task<string?> ResolveNameToPhoneAsync(string chatId, string name, string? preferSenderId, CancellationToken ct);
}
