using Lis.Core.Configuration;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lis.Agent.Commands;

public sealed class CompactCommand(CompactionService compactionService, IOptions<LisOptions> lisOptions) : IChatCommand {
	public string[] Triggers => ["/compact"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Session is null)
			return "No active session to compact.";

		if (ctx.Session.IsCompacting)
			return "Compaction already in progress.";

		List<MessageEntity> allMsgs = await ctx.Db.Messages
			.Where(m => m.ChatId == ctx.Chat.Id
			         && (ctx.Session.StartMessageId == null || m.Id >= ctx.Session.StartMessageId))
			.OrderByDescending(m => m.Id)
			.ToListAsync(ct);

		if (allMsgs.Count < 2)
			return "Not enough messages to compact.";

		long splitId = CompactionService.CalculateSplitPoint(allMsgs, lisOptions.Value.KeepRecentTokens);

		long oldSessionId = ctx.Session.Id;
		string chatExternalId = ctx.Chat.ExternalId;

		_ = Task.Run(
			() => compactionService.CompactAsync(chatExternalId, splitId, CancellationToken.None),
			CancellationToken.None);

		return $"⚙️ Compacting session #{oldSessionId}...";
	}
}
