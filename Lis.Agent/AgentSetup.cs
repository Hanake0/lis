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
			// Short pluginName keeps tool names compact (e.g. "dt_get_current_datetime").
			kernel.Plugins.AddFromType<DateTimePlugin>(pluginName: "dt", serviceProvider: sp);
			kernel.Plugins.AddFromType<PromptPlugin>(pluginName: "prompt", serviceProvider: sp);
			kernel.Plugins.AddFromType<MemoryPlugin>(pluginName: "mem", serviceProvider: sp);
			kernel.Plugins.AddFromType<ConfigPlugin>(pluginName: "cfg", serviceProvider: sp);
			kernel.Plugins.AddFromType<ResponsePlugin>(pluginName: "resp", serviceProvider: sp);
			kernel.Plugins.AddFromType<ExecPlugin>(pluginName: "exec", serviceProvider: sp);
			kernel.Plugins.AddFromType<FileSystemPlugin>(pluginName: "fs", serviceProvider: sp);
			kernel.Plugins.AddFromType<WebPlugin>(pluginName: "web", serviceProvider: sp);

			// Build auth registry from plugin metadata
			ToolAuthRegistry authRegistry = sp.GetRequiredService<ToolAuthRegistry>();
			authRegistry.Build(kernel);

			return kernel;
		});

		// Tool authorization, policy, and approvals
		services.AddSingleton<ToolAuthRegistry>();
		services.AddSingleton<ToolPolicyService>();
		services.AddSingleton<IApprovalService, ApprovalService>();

		// Agent
		services.AddSingleton<AgentService>();

		// Commands
		services.AddSingleton<IChatCommand, StatusCommand>();
		services.AddSingleton<IChatCommand, NewSessionCommand>();
		services.AddSingleton<IChatCommand, CompactCommand>();
		services.AddSingleton<IChatCommand, PruneToolsCommand>();
		services.AddSingleton<IChatCommand, ResumeCommand>();
		services.AddSingleton<IChatCommand, AbortCommand>();
		services.AddSingleton<IChatCommand, AgentCommand>();
		services.AddSingleton<IChatCommand, AgentsCommand>();
		services.AddSingleton<IChatCommand, ModelCommand>();
		services.AddSingleton<IChatCommand, ModelsCommand>();
		services.AddSingleton<IChatCommand, ApproveCommand>();
		services.AddSingleton<IChatCommand, DenyCommand>();
		services.AddSingleton<CommandRouter>();

		// Media
		services.AddScoped<IMediaProcessor, MediaProcessor>();

		// Compaction
		services.AddSingleton<CompactionService>();

		return services;
	}
}
