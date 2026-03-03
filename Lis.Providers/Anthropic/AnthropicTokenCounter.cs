using System.Text;
using System.Text.Json.Nodes;

using Lis.Core.Channel;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Counts tokens using Anthropic's free /v1/messages/count_tokens endpoint.
/// Takes a pre-built JSON request body (same format as /v1/messages) and returns input_tokens.
/// </summary>
public sealed class AnthropicTokenCounter(HttpClient httpClient) : ITokenCounter {
	private const string Endpoint = "https://api.anthropic.com/v1/messages/count_tokens";

	public async Task<int?> CountAsync(string requestBodyJson, CancellationToken ct = default) {
		try {
			using StringContent content = new(requestBodyJson, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await httpClient.PostAsync(Endpoint, content, ct);
			response.EnsureSuccessStatusCode();
			string json = await response.Content.ReadAsStringAsync(ct);
			return JsonNode.Parse(json)?["input_tokens"]?.GetValue<int>();
		} catch {
			return null;
		}
	}
}
