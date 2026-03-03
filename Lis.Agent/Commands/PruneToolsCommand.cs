using Microsoft.EntityFrameworkCore;

namespace Lis.Agent.Commands;

public sealed class PruneToolsCommand : IChatCommand {
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

		ctx.Session.ToolsPrunedThroughId = lastMsgId;
		await ctx.Db.SaveChangesAsync(ct);

		return "Tool results pruned.";
	}
}
