using Lis.Core.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Lis.Agent.Commands;

public sealed class PruneToolsCommand(ModelSettings modelSettings) : IChatCommand {
	public string[] Triggers => ["/prune", "/prune-tools"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Session is null)
			return "No active session.";

		long? lastMsgId = await ctx.Db.Messages
			.Where(m => m.ChatId == ctx.Chat.Id)
			.OrderByDescending(m => m.Id)
			.Select(m => (long?)m.Id)
			.FirstOrDefaultAsync(ct);

		if (lastMsgId is null)
			return "No messages to prune.";

		if (ctx.Session.ToolsPrunedThroughId >= lastMsgId)
			return "Tools already pruned up to the latest message.";

		// Gather stats before pruning
		int toolTokens = await ctx.Db.Messages
			.Where(m => m.SessionId == ctx.Session.Id && !m.Queued && m.Role == "tool")
			.SumAsync(m => m.OutputTokens ?? 0, ct);
		int toolCount = await ctx.Db.Messages
			.Where(m => m.SessionId == ctx.Session.Id && !m.Queued && m.Role == "tool")
			.CountAsync(ct);

		ctx.Session.ToolsPrunedThroughId = lastMsgId;
		await ctx.Db.SaveChangesAsync(ct);

		int prunedEstimate = toolCount * 10;
		long totalInput    = ctx.Session.ContextTokens;
		int savings        = toolTokens > 0 ? (int)((long)(toolTokens - prunedEstimate) * 100 / toolTokens) : 0;
		long newContext    = totalInput - toolTokens + prunedEstimate;
		int budget         = modelSettings.ContextBudget;
		int pct            = budget > 0 ? (int)(newContext * 100 / budget) : 0;

		return $"🔧 Tool outputs pruned ({Fmt(toolTokens)} → {Fmt(prunedEstimate)}, -{savings}%)"
		     + $"\n  📊 Context: {Fmt(newContext)}/{Fmt(budget)} ({pct}%)";
	}

	private static string Fmt(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";
}
