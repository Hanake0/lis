namespace Lis.Core.Channel;

public interface ITokenCounter {
	Task<int> CountAsync(string systemPrompt, IReadOnlyList<object> messages, CancellationToken ct);
}
