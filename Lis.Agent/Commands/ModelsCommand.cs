using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lis.Agent.Commands;

public sealed class ModelsCommand(IServiceScopeFactory scopeFactory) : IChatCommand {
	public string[] Triggers => ["/models"];

	private static readonly string[] KnownModels = [
		"claude-opus-4-6",
		"claude-sonnet-4-6",
		"claude-haiku-4-5",
		"claude-sonnet-4-5-20250514"
	];

	[Trace("ModelsCommand > ExecuteAsync")]
	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<AgentEntity> agents = await db.Agents
			.OrderBy(a => a.Id)
			.ToListAsync(ct);

		StringBuilder sb = new();
		sb.AppendLine("🧠 Available models:");
		sb.AppendLine();

		foreach (string model in KnownModels) {
			List<string> usedBy = agents
				.Where(a => a.Model == model)
				.Select(a => a.Name)
				.ToList();

			string usage = usedBy.Count > 0
				? $" ← {string.Join(", ", usedBy)}"
				: "";

			sb.AppendLine($"- {model}{usage}");
		}

		return sb.ToString().TrimEnd();
	}
}
