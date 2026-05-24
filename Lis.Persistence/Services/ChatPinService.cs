using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence.Services;

/// <summary>
/// Pin / unpin a chat by setting <see cref="ChatEntity.PinnedAt"/>. The actual sort
/// order is decided by the client (pinned chats bubble to the top of their group).
/// </summary>
public interface IChatPinService {
	Task<ChatEntity> PinAsync(string chatExternalId, CancellationToken ct = default);
	Task<ChatEntity> UnpinAsync(string chatExternalId, CancellationToken ct = default);
}

public sealed class ChatPinService(LisDbContext db, TimeProvider time) :IChatPinService {

	[Trace("ChatPinService > PinAsync")]
	public async Task<ChatEntity> PinAsync(string chatExternalId, CancellationToken ct = default) {
		ChatEntity chat = await this.RequireAsync(chatExternalId, ct);
		// Idempotent on the pinned bit, but always refresh the timestamp so clients
		// that order by pinned_at-desc see freshly pinned chats at the top.
		chat.PinnedAt  = time.GetUtcNow();
		chat.UpdatedAt = chat.PinnedAt.Value;
		await db.SaveChangesAsync(ct);
		return chat;
	}

	[Trace("ChatPinService > UnpinAsync")]
	public async Task<ChatEntity> UnpinAsync(string chatExternalId, CancellationToken ct = default) {
		ChatEntity chat = await this.RequireAsync(chatExternalId, ct);
		if (chat.PinnedAt is null) return chat; // already unpinned — no-op without bumping updated_at

		chat.PinnedAt  = null;
		chat.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return chat;
	}

	private async Task<ChatEntity> RequireAsync(string chatExternalId, CancellationToken ct) =>
		await db.Chats.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId, ct)
			?? throw new KeyNotFoundException($"Chat '{chatExternalId}' not found");
}
