using System.Text;

using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lis.Agent.Commands;

public sealed class AgentCommand(
	IServiceScopeFactory scopeFactory,
	CompactionService    compactionService,
	IOptions<LisOptions> lisOptions) : IChatCommand {

	public string[] Triggers => ["/agent"];

	[Trace("AgentCommand > ExecuteAsync")]
	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Args is null or { Length: 0 })
			return this.ShowCurrentAgent(ctx);

		string[] parts = ctx.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string subcommand = parts[0].ToLowerInvariant();

		if (subcommand == "new" && parts.Length >= 2)
			return await this.CreateAgentAsync(ctx, parts[1], parts.Length >= 3 ? string.Join(' ', parts[2..]) : null, ct);

		if (subcommand == "delete" && parts.Length >= 2)
			return await this.DeleteAgentAsync(ctx, parts[1], ct);

		// Otherwise treat as agent name to switch to
		return await this.SwitchAgentAsync(ctx, ctx.Args.Trim(), ct);
	}

	private string ShowCurrentAgent(CommandContext ctx) {
		StringBuilder sb = new();
		sb.AppendLine($"🤖 Agent: {ctx.Agent.Name}");
		sb.AppendLine($"📛 Display name: {ctx.Agent.DisplayName ?? "(none)"}");
		sb.Append($"🧠 Model: {ctx.Agent.Model}");
		return sb.ToString();
	}

	[Trace("AgentCommand > SwitchAgentAsync")]
	private async Task<string> SwitchAgentAsync(CommandContext ctx, string name, CancellationToken ct) {
		AgentEntity? agent = await ctx.Db.Agents
			.FirstOrDefaultAsync(a => a.Name == name, ct);

		if (agent is null)
			return $"Agent '{name}' not found.";

		if (ctx.Chat.AgentId == agent.Id)
			return $"Already using agent '{name}'.";

		ctx.Chat.AgentId = agent.Id;
		ctx.Chat.Agent   = agent;

		if (lisOptions.Value.NewSessionOnAgentSwitch) {
			await compactionService.StartNewSessionAsync(
				ctx.Chat, ctx.Session, isExplicitBreak: true, ctx.Db, ct);
		}

		await ctx.Db.SaveChangesAsync(ct);

		return $"✅ Switched to agent '{agent.DisplayName ?? agent.Name}'.";
	}

	[Trace("AgentCommand > CreateAgentAsync")]
	private async Task<string> CreateAgentAsync(CommandContext ctx, string name, string? displayName, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		bool exists = await db.Agents.AnyAsync(a => a.Name == name, ct);
		if (exists)
			return $"Agent '{name}' already exists.";

		AgentEntity newAgent = new() {
			Name                    = name,
			DisplayName             = displayName,
			Model                   = ctx.Agent.Model,
			MaxTokens               = ctx.Agent.MaxTokens,
			ContextBudget           = ctx.Agent.ContextBudget,
			ThinkingEffort          = ctx.Agent.ThinkingEffort,
			ToolNotifications       = ctx.Agent.ToolNotifications,
			CompactionThreshold     = ctx.Agent.CompactionThreshold,
			KeepRecentTokens        = ctx.Agent.KeepRecentTokens,
			ToolPruneThreshold      = ctx.Agent.ToolPruneThreshold,
			ToolKeepThreshold       = ctx.Agent.ToolKeepThreshold,
			ToolSummarizationPolicy = ctx.Agent.ToolSummarizationPolicy,
			IsDefault               = false,
			CreatedAt               = DateTimeOffset.UtcNow,
			UpdatedAt               = DateTimeOffset.UtcNow
		};

		db.Agents.Add(newAgent);
		await db.SaveChangesAsync(ct);

		// Copy prompt sections from current agent
		List<PromptSectionEntity> sections = await db.PromptSections
			.Where(s => s.AgentId == ctx.Agent.Id)
			.ToListAsync(ct);

		foreach (PromptSectionEntity section in sections) {
			db.PromptSections.Add(new PromptSectionEntity {
				AgentId   = newAgent.Id,
				Name      = section.Name,
				Content   = section.Content,
				SortOrder = section.SortOrder,
				IsEnabled = section.IsEnabled,
				CreatedAt = DateTimeOffset.UtcNow,
				UpdatedAt = DateTimeOffset.UtcNow
			});
		}

		await db.SaveChangesAsync(ct);

		return $"✅ Agent '{name}' created with {sections.Count} prompt sections copied.";
	}

	[Trace("AgentCommand > DeleteAgentAsync")]
	private async Task<string> DeleteAgentAsync(CommandContext ctx, string name, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		AgentEntity? agent = await db.Agents
			.FirstOrDefaultAsync(a => a.Name == name, ct);

		if (agent is null)
			return $"Agent '{name}' not found.";

		if (agent.IsDefault)
			return "Cannot delete the default agent.";

		// Get default agent for reassignment
		AgentEntity? defaultAgent = await db.Agents
			.FirstOrDefaultAsync(a => a.IsDefault, ct);

		if (defaultAgent is null)
			return "No default agent found for reassignment.";

		// Reassign chats using this agent to default
		int reassigned = await db.Chats
			.Where(c => c.AgentId == agent.Id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(c => c.AgentId, defaultAgent.Id), ct);

		db.Agents.Remove(agent);
		await db.SaveChangesAsync(ct);

		return $"✅ Agent '{name}' deleted. {reassigned} chat(s) reassigned to '{defaultAgent.Name}'.";
	}
}
