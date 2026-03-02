using System.Net.Http.Headers;

using Anthropic.SDK;

using Lis.Core.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lis.Providers.Anthropic;

public static class AnthropicProvider {
	public static IServiceCollection AddAnthropic(this IServiceCollection services) {
		AnthropicOptions opts = new() {
			ApiKey        = Env("ANTHROPIC_API_KEY"),
			Model         = Env("ANTHROPIC_MODEL") is { Length: > 0 } m ? m : "claude-sonnet-4-20250514",
			MaxTokens     = EnvInt("ANTHROPIC_MAX_TOKENS", 4096),
			ContextBudget = EnvInt("ANTHROPIC_CONTEXT_BUDGET", 12000),
		};

		bool useBearer = Env("ANTHROPIC_AUTH_MODE").Equals("bearer", StringComparison.OrdinalIgnoreCase)
		              || opts.ApiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);

		AnthropicClient anthropic = useBearer
			? new AnthropicClient(opts.ApiKey, new HttpClient(new BearerAuthHandler(opts.ApiKey) { InnerHandler = new HttpClientHandler() }))
			: new AnthropicClient(opts.ApiKey);

		services.AddSingleton(Options.Create(opts));
		services.AddSingleton(new ModelSettings { Model = opts.Model, MaxTokens = opts.MaxTokens, ContextBudget = opts.ContextBudget });
		services.AddSingleton<IChatClient>(anthropic.Messages);

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
}
