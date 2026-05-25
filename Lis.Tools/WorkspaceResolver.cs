using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lis.Tools;

/// <summary>
/// Resolves the working directory tools should treat as their cwd. Centralised
/// so every plugin (exec, filesystem, future git/etc.) honours the same
/// resolution order — and so the new chat-scoped <c>directory</c> override
/// added in Stage 15c doesn't have to be remembered in N call sites.
///
/// Order (first non-null wins):
///   1. <c>chat.Directory</c> — explicit per-chat working-directory override
///      set via PATCH /api/chats/{id} from the composer's worktree pill.
///   2. <c>chat.Project.Path</c> — project the chat is bound to.
///   3. <c>agent.WorkspacePath</c> — legacy per-agent default (kept as a
///      fallback so chats with no project still resolve sensibly).
///   4. <c>Directory.GetCurrentDirectory()</c> — last-resort process cwd.
/// </summary>
public static class WorkspaceResolver {

	/// <summary>
	/// Resolve the effective workspace cwd for the current tool call, using the
	/// active <see cref="ToolContext"/> as the source of identity (ChatId +
	/// AgentId). Throws if AgentId is missing — every legitimate tool call has
	/// one. ChatId is optional (e.g. scheduled-task one-shots) — when absent
	/// the resolver skips straight to the agent's WorkspacePath.
	/// </summary>
	[Trace("WorkspaceResolver > Resolve")]
	public static async Task<string> ResolveAsync(
		IServiceScopeFactory scopeFactory,
		CancellationToken    ct = default
	) {
		long agentId = ToolContext.AgentId
			?? throw new InvalidOperationException("WorkspaceResolver requires ToolContext.AgentId");

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string? chatId = ToolContext.ChatId;
		if (!string.IsNullOrEmpty(chatId)) {
			ChatEntity? chat = await db.Chats
				.Include(c => c.Project)
				.AsNoTracking()
				.FirstOrDefaultAsync(c => c.ExternalId == chatId, ct);

			if (!string.IsNullOrWhiteSpace(chat?.Directory)) return chat.Directory;
			if (!string.IsNullOrWhiteSpace(chat?.Project?.Path)) return chat.Project.Path;
		}

		AgentEntity? agent = await db.Agents.FindAsync([agentId], ct);
		return agent?.WorkspacePath ?? Directory.GetCurrentDirectory();
	}
}
