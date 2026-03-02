using Lis.Tools;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public static class AgentSetup {
	public static IServiceCollection AddLisAgent(this IServiceCollection services) {
		services.AddSingleton<Kernel>(sp => {
			IChatClient        chatClient    = sp.GetRequiredService<IChatClient>();
			IServiceScopeFactory scopeFactory  = sp.GetRequiredService<IServiceScopeFactory>();
			ILoggerFactory       loggerFactory = sp.GetRequiredService<ILoggerFactory>();

			IKernelBuilder builder = Kernel.CreateBuilder();
			builder.Services.AddSingleton<IChatCompletionService>(chatClient.AsChatCompletionService());
			builder.Services.AddSingleton(scopeFactory);
			builder.Services.AddSingleton(loggerFactory);
			builder.Plugins.AddFromType<DateTimePlugin>();
			builder.Plugins.AddFromType<MemoryPlugin>();
			builder.Plugins.AddFromType<PromptPlugin>();

			return builder.Build();
		});

		return services;
	}
}
