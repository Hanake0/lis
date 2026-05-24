using System.Text;
using System.Text.Json.Nodes;

using Lis.Core.Channel;

using Microsoft.Extensions.Logging;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Counts tokens via Anthropic's free <c>/v1/messages/count_tokens</c> endpoint.
/// Takes a pre-built JSON request body (same format as <c>/v1/messages</c>) and returns
/// <c>input_tokens</c>. If the free endpoint fails (network, beta config mismatch, etc.),
/// falls back to a real <c>/v1/messages</c> call on a small/fast model with
/// <c>max_tokens=1</c> and extracts <c>usage.input_tokens</c> from the response —
/// same pattern Claude Code uses in <c>tokenEstimation.countTokensViaHaikuFallback</c>.
/// The fallback consumes a tiny amount of quota; the primary path is free.
/// </summary>
public sealed class AnthropicTokenCounter(HttpClient httpClient, ILogger<AnthropicTokenCounter>? logger = null) : ITokenCounter {
	private const string CountEndpoint   = "https://api.anthropic.com/v1/messages/count_tokens";
	private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
	// Small, fast model for the fallback. Tokenizer is the same across Claude models,
	// so this doesn't affect the count — it just minimizes latency and quota burn.
	private const string FallbackModel = "claude-haiku-4-5-20251001";

	public async Task<int?> CountAsync(string requestBodyJson, CancellationToken ct = default) {
		int? primary = await this.TryCountTokensAsync(requestBodyJson, ct);
		if (primary is not null) return primary;

		logger?.LogDebug("count_tokens failed, falling back to messages.create(max_tokens=1)");
		return await this.TryFallbackAsync(requestBodyJson, ct);
	}

	private async Task<int?> TryCountTokensAsync(string requestBodyJson, CancellationToken ct) {
		try {
			using StringContent content = new(requestBodyJson, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await httpClient.PostAsync(CountEndpoint, content, ct);
			if (!response.IsSuccessStatusCode) {
				if (logger is not null && logger.IsEnabled(LogLevel.Debug))
					logger.LogDebug("count_tokens non-success: {Status}", (int)response.StatusCode);
				return null;
			}
			string json = await response.Content.ReadAsStringAsync(ct);
			return JsonNode.Parse(json)?["input_tokens"]?.GetValue<int>();
		} catch (Exception ex) {
			logger?.LogDebug(ex, "count_tokens threw");
			return null;
		}
	}

	private async Task<int?> TryFallbackAsync(string requestBodyJson, CancellationToken ct) {
		try {
			JsonNode? body = JsonNode.Parse(requestBodyJson);
			if (body is not JsonObject obj) return null;
			obj["model"]      = FallbackModel;
			obj["max_tokens"] = 1;
			// Strip count_tokens-only fields if present — they'd 400 on /v1/messages.
			obj.Remove("metadata");

			using StringContent content = new(obj.ToJsonString(), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await httpClient.PostAsync(MessagesEndpoint, content, ct);
			if (!response.IsSuccessStatusCode) {
				logger?.LogWarning("Token-count fallback non-success: {Status}", (int)response.StatusCode);
				return null;
			}
			string json = await response.Content.ReadAsStringAsync(ct);
			JsonNode? root = JsonNode.Parse(json);
			JsonNode? usage = root?["usage"];
			int input  = usage?["input_tokens"]?.GetValue<int>() ?? 0;
			int cRead  = usage?["cache_read_input_tokens"]?.GetValue<int>() ?? 0;
			int cWrite = usage?["cache_creation_input_tokens"]?.GetValue<int>() ?? 0;
			int total = input + cRead + cWrite;
			return total > 0 ? total : null;
		} catch (Exception ex) {
			logger?.LogWarning(ex, "Token-count fallback threw");
			return null;
		}
	}
}
