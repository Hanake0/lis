namespace Lis.Core.Channel;

/// <summary>
/// Counts input tokens using the provider's token counting endpoint.
/// Takes a pre-built JSON request body and returns the token count.
/// </summary>
public interface ITokenCounter {
	Task<int?> CountAsync(string requestBodyJson, CancellationToken ct = default);
}
