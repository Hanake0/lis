using Lis.Persistence.Entities;

namespace Lis.Agent.Commands;

public sealed class NewSessionCommand(CompactionService compactionService) : IChatCommand {
	public string[] Triggers => ["/new", "/clear"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		long? oldSessionId = ctx.Session?.Id;

		// Finalize current session asynchronously (summary + embedding)
		// and create new session with no parent (explicit break)
		SessionEntity newSession = await compactionService.StartNewSessionAsync(
			ctx.Chat, ctx.Session, isExplicitBreak: true, ctx.Db, ct);

		return oldSessionId is not null
			? $"✨ New session started. Previous session #{oldSessionId} archived."
			: $"✨ New session #{newSession.Id} started.";
	}
}
