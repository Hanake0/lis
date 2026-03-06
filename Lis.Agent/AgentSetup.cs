using Lis.Agent.Commands;
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
			IChatClient    chatClient    = sp.GetRequiredService<IChatClient>();
			ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

			IKernelBuilder builder = Kernel.CreateBuilder();
			builder.Services.AddSingleton<IChatCompletionService>(chatClient.AsChatCompletionService());
			builder.Services.AddSingleton(loggerFactory);

			Kernel kernel = builder.Build();

			// Register plugins using the OUTER service provider (has LisDbContext, embeddings, etc.)
			// builder.Plugins.AddFromType<T>() would resolve from the kernel's internal provider,
			// which shadows IServiceScopeFactory with its own built-in implementation.
			kernel.Plugins.AddFromType<DateTimePlugin>(pluginName: null, serviceProvider: sp);
			kernel.Plugins.AddFromType<PromptPlugin>(pluginName: null, serviceProvider: sp);
			kernel.Plugins.AddFromType<MemoryPlugin>(pluginName: null, serviceProvider: sp);

			return kernel;
		});

		// Commands
		services.AddSingleton<IChatCommand, StatusCommand>();
		services.AddSingleton<IChatCommand, NewSessionCommand>();
		services.AddSingleton<IChatCommand, CompactCommand>();
		services.AddSingleton<IChatCommand, PruneToolsCommand>();
		services.AddSingleton<IChatCommand, ResumeCommand>();
		services.AddSingleton<IChatCommand, AbortCommand>();
		services.AddSingleton<CommandRouter>();

		// Compaction
		services.AddSingleton<CompactionService>();

		return services;
	}
}
