using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lis.Agent.Commands;

public sealed class AgentsCommand(IServiceScopeFactory scopeFactory) : IChatCommand {
	public string[] Triggers => ["/agents"];

	[Trace("AgentsCommand > ExecuteAsync")]
	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<AgentEntity> agents = await db.Agents
			.OrderBy(a => a.Id)
			.ToListAsync(ct);

		if (agents.Count == 0)
			return "No agents found.";

		StringBuilder sb = new();
		sb.AppendLine("📋 Agents:");
		sb.AppendLine();

		foreach (AgentEntity agent in agents) {
			int chatCount = await db.Chats.CountAsync(c => c.AgentId == agent.Id, ct);
			string defaultIndicator = agent.IsDefault ? " (default)" : "";
			sb.AppendLine($"- {agent.Name} · {agent.Model} · {chatCount} chat(s){defaultIndicator}");
		}

		return sb.ToString().TrimEnd();
	}
}
