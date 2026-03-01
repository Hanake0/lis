namespace Lis.Core.Channel;

public interface IConversationService {
	Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct);
}
