using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Anthropic.SDK;

using Lis.Core.Channel;
using Lis.Core.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lis.Providers.Anthropic;

public static class AnthropicProvider {
	public static IServiceCollection AddAnthropic(this IServiceCollection services) {
		AnthropicOptions opts = new() {
			ApiKey         = Env("ANTHROPIC_API_KEY"),
			Model          = Env("ANTHROPIC_MODEL") is { Length: > 0 } m ? m : "claude-sonnet-4-20250514",
			MaxTokens      = EnvInt("ANTHROPIC_MAX_TOKENS", 4096),
			ContextBudget  = EnvInt("ANTHROPIC_CONTEXT_BUDGET", 12000),
			ThinkingEffort = Env("ANTHROPIC_THINKING_EFFORT") is { Length: > 0 } te ? te : null,
			CacheEnabled   = Env("ANTHROPIC_CACHE_ENABLED") != "false",
			CacheTtl       = Env("ANTHROPIC_CACHE_TTL") is { Length: > 0 } ttl ? ttl : "5m",
		};

		bool useBearer = Env("ANTHROPIC_AUTH_MODE").Equals("bearer", StringComparison.OrdinalIgnoreCase)
		              || opts.ApiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);

		HttpMessageHandler innerHandler = new HttpClientHandler();

		if (opts.CacheEnabled)
			innerHandler = new CacheControlHandler(opts.CacheTtl) { InnerHandler = innerHandler };

		AnthropicClient anthropic = useBearer
			? new AnthropicClient(opts.ApiKey, new HttpClient(new BearerAuthHandler(opts.ApiKey) { InnerHandler = innerHandler }))
			: new AnthropicClient(opts.ApiKey, new HttpClient(innerHandler));

		services.AddSingleton(Options.Create(opts));
		services.AddSingleton(new ModelSettings {
			Model = opts.Model, MaxTokens = opts.MaxTokens,
			ContextBudget = opts.ContextBudget, ThinkingEffort = opts.ThinkingEffort
		});
		services.AddSingleton<IChatClient>(anthropic.Messages);
		services.AddSingleton<IUsageExtractor, AnthropicUsageExtractor>();

		return services;
	}

	private static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? "";

	private static int EnvInt(string key, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(key), out int v) ? v : fallback;

	private sealed class BearerAuthHandler(string token) : DelegatingHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			request.Headers.Remove("x-api-key");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Headers.TryAddWithoutValidation("user-agent", "claude-cli/2.1.62");
			request.Headers.TryAddWithoutValidation("x-app", "cli");
			request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
			request.Headers.TryAddWithoutValidation("anthropic-beta",
				"claude-code-20250219,oauth-2025-04-20,interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14");
			return base.SendAsync(request, ct);
		}
	}

	/// <summary>
	/// Injects cache_control markers into Anthropic API requests for prompt caching.
	/// Adds top-level automatic caching + explicit breakpoint on the last system content block.
	/// </summary>
	private sealed class CacheControlHandler(string cacheTtl) : DelegatingHandler {
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			if (request.Content is null || request.RequestUri?.AbsolutePath.Contains("/messages") != true)
				return await base.SendAsync(request, ct);

			string json = await request.Content.ReadAsStringAsync(ct);
			string modified = InjectCacheControl(json, cacheTtl);
			request.Content = new StringContent(modified, Encoding.UTF8, "application/json");

			return await base.SendAsync(request, ct);
		}

		private static string InjectCacheControl(string json, string ttl) {
			JsonNode? root = JsonNode.Parse(json);
			if (root is not JsonObject obj) return json;

			JsonObject cacheControl = ttl == "1h"
				? new JsonObject { ["type"] = "ephemeral", ["ttl"] = "1h" }
				: new JsonObject { ["type"] = "ephemeral" };

			// Top-level automatic caching (breakpoint #4 — auto-moves to last cacheable block)
			obj["cache_control"] = cacheControl.DeepClone();

			// Explicit breakpoint on last system content block (breakpoint #1)
			if (obj["system"] is JsonArray systemArr && systemArr.Count > 0) {
				JsonNode? lastBlock = systemArr[^1];
				if (lastBlock is JsonObject lastObj)
					lastObj["cache_control"] = cacheControl.DeepClone();
			}

			return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
		}
	}
}
