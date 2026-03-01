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

		services.AddSingleton(Options.Create(opts));
		services.AddSingleton(new ModelSettings { MaxTokens = opts.MaxTokens, ContextBudget = opts.ContextBudget });
		services.AddSingleton<IChatClient>(new ChatClientBuilder(new AnthropicClient(opts.ApiKey).Messages).UseFunctionInvocation().Build());

		return services;
	}

	private static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? "";

	private static int EnvInt(string key, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(key), out int v) ? v : fallback;
}
