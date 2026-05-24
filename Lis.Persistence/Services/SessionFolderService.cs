using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence.Services;

/// <summary>
/// CRUD plus membership operations for <see cref="SessionFolderEntity"/>. The backend
/// is scope-agnostic: callers pass an arbitrary <c>scopeKey</c> string (typically
/// <c>dir:/path</c>, <c>__archived__:/path</c>, or <c>__global__</c>) and we just
/// store and filter on it.
/// </summary>
public interface ISessionFolderService {
	Task<IReadOnlyList<SessionFolderEntity>> ListForScopeAsync(string scopeKey, CancellationToken ct = default);
	Task<SessionFolderEntity?>               GetByExternalIdAsync(string externalId, CancellationToken ct = default);
	Task<SessionFolderEntity>                CreateAsync(string scopeKey, string name, string? parentExternalId = null, CancellationToken ct = default);
	Task<SessionFolderEntity>                RenameAsync(string externalId, string name, CancellationToken ct = default);
	Task                                     DeleteAsync(string externalId, CancellationToken ct = default);
	Task                                     ReorderAsync(string scopeKey, IReadOnlyList<string> orderedExternalIds, CancellationToken ct = default);

	/// <summary>Append chats to a folder, ignoring duplicates. Returns the now-current item list.</summary>
	Task<IReadOnlyList<SessionFolderItemEntity>> AddSessionsAsync(string folderExternalId, IReadOnlyList<string> chatExternalIds, CancellationToken ct = default);

	Task RemoveSessionAsync(string folderExternalId, string chatExternalId, CancellationToken ct = default);

	/// <summary>Removes folder items whose chat_external_id no longer exists in the chat table.</summary>
	Task<int> CleanupAsync(CancellationToken ct = default);
}

public sealed class SessionFolderService(LisDbContext db, TimeProvider time) :ISessionFolderService {

	[Trace("SessionFolderService > ListForScopeAsync")]
	public async Task<IReadOnlyList<SessionFolderEntity>> ListForScopeAsync(string scopeKey, CancellationToken ct = default) =>
		await db.SessionFolders
				.AsNoTracking()
				.Where(f => f.ScopeKey == scopeKey)
				.OrderBy(f => f.ParentExternalId)
				.ThenBy(f => f.SortIndex)
				.ThenBy(f => f.CreatedAt)
				.Include(f => f.Items.OrderBy(i => i.SortIndex))
				.ToListAsync(ct);

	[Trace("SessionFolderService > GetByExternalIdAsync")]
	public Task<SessionFolderEntity?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
		db.SessionFolders.Include(f => f.Items).FirstOrDefaultAsync(f => f.ExternalId == externalId, ct);

	[Trace("SessionFolderService > CreateAsync")]
	public async Task<SessionFolderEntity> CreateAsync(string scopeKey, string name, string? parentExternalId = null, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("scopeKey is required", nameof(scopeKey));
		if (string.IsNullOrWhiteSpace(name))     throw new ArgumentException("name is required",     nameof(name));

		if (parentExternalId is not null) {
			SessionFolderEntity? parent = await db.SessionFolders.FirstOrDefaultAsync(f => f.ExternalId == parentExternalId, ct)
				?? throw new KeyNotFoundException($"Parent folder '{parentExternalId}' not found");
			if (parent.ScopeKey != scopeKey)
				throw new ArgumentException("Parent folder belongs to a different scope", nameof(parentExternalId));
		}

		int maxSort = await db.SessionFolders
			.Where(f => f.ScopeKey == scopeKey && f.ParentExternalId == parentExternalId)
			.MaxAsync(f => (int?)f.SortIndex, ct) ?? -1;

		DateTimeOffset now = time.GetUtcNow();
		SessionFolderEntity folder = new() {
			ExternalId       = Guid.NewGuid().ToString("N")[..16],
			ScopeKey         = scopeKey,
			Name             = name.Trim(),
			ParentExternalId = parentExternalId,
			SortIndex        = maxSort + 1,
			CreatedAt        = now,
			UpdatedAt        = now,
		};
		db.SessionFolders.Add(folder);
		await db.SaveChangesAsync(ct);
		return folder;
	}

	[Trace("SessionFolderService > RenameAsync")]
	public async Task<SessionFolderEntity> RenameAsync(string externalId, string name, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));

		SessionFolderEntity folder = await this.RequireAsync(externalId, ct);
		folder.Name      = name.Trim();
		folder.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return folder;
	}

	[Trace("SessionFolderService > DeleteAsync")]
	public async Task DeleteAsync(string externalId, CancellationToken ct = default) {
		SessionFolderEntity folder = await this.RequireAsync(externalId, ct);
		// Items cascade via FK; nested folders are NOT cascaded — orphan them so the
		// frontend can decide whether to re-attach to root or delete them too.
		List<SessionFolderEntity> children = await db.SessionFolders
			.Where(f => f.ParentExternalId == externalId)
			.ToListAsync(ct);
		foreach (SessionFolderEntity child in children) {
			child.ParentExternalId = null;
			child.UpdatedAt        = time.GetUtcNow();
		}
		db.SessionFolders.Remove(folder);
		await db.SaveChangesAsync(ct);
	}

	[Trace("SessionFolderService > ReorderAsync")]
	public async Task ReorderAsync(string scopeKey, IReadOnlyList<string> orderedExternalIds, CancellationToken ct = default) {
		if (orderedExternalIds.Count == 0) return;

		List<SessionFolderEntity> folders = await db.SessionFolders
			.Where(f => f.ScopeKey == scopeKey && orderedExternalIds.Contains(f.ExternalId))
			.ToListAsync(ct);

		DateTimeOffset now = time.GetUtcNow();
		for (int i = 0; i < orderedExternalIds.Count; i++) {
			SessionFolderEntity? f = folders.FirstOrDefault(x => x.ExternalId == orderedExternalIds[i]);
			if (f is null) continue;
			if (f.SortIndex != i) {
				f.SortIndex = i;
				f.UpdatedAt = now;
			}
		}
		await db.SaveChangesAsync(ct);
	}

	[Trace("SessionFolderService > AddSessionsAsync")]
	public async Task<IReadOnlyList<SessionFolderItemEntity>> AddSessionsAsync(string folderExternalId, IReadOnlyList<string> chatExternalIds, CancellationToken ct = default) {
		SessionFolderEntity folder = await this.RequireAsync(folderExternalId, ct);

		HashSet<string> existing = (await db.SessionFolderItems
			.Where(i => i.FolderId == folder.Id)
			.Select(i => i.ChatExternalId)
			.ToListAsync(ct)).ToHashSet();

		int maxSort = await db.SessionFolderItems
			.Where(i => i.FolderId == folder.Id)
			.MaxAsync(i => (int?)i.SortIndex, ct) ?? -1;

		DateTimeOffset now = time.GetUtcNow();
		int next = maxSort + 1;
		foreach (string chatId in chatExternalIds) {
			if (string.IsNullOrWhiteSpace(chatId) || !existing.Add(chatId)) continue;
			db.SessionFolderItems.Add(new SessionFolderItemEntity {
				FolderId       = folder.Id,
				ChatExternalId = chatId,
				SortIndex      = next++,
				AddedAt        = now,
			});
		}

		folder.UpdatedAt = now;
		await db.SaveChangesAsync(ct);

		return await db.SessionFolderItems
			.Where(i => i.FolderId == folder.Id)
			.OrderBy(i => i.SortIndex)
			.ToListAsync(ct);
	}

	[Trace("SessionFolderService > RemoveSessionAsync")]
	public async Task RemoveSessionAsync(string folderExternalId, string chatExternalId, CancellationToken ct = default) {
		SessionFolderEntity folder = await this.RequireAsync(folderExternalId, ct);
		SessionFolderItemEntity? item = await db.SessionFolderItems
			.FirstOrDefaultAsync(i => i.FolderId == folder.Id && i.ChatExternalId == chatExternalId, ct);
		if (item is null) return; // idempotent — no-op when already absent

		db.SessionFolderItems.Remove(item);
		folder.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
	}

	[Trace("SessionFolderService > CleanupAsync")]
	public async Task<int> CleanupAsync(CancellationToken ct = default) {
		// Pull all current chat external ids in one query then diff in memory; folder items
		// table is small enough that this is cheaper than an EXCEPT subquery on Npgsql.
		HashSet<string> chatIds = (await db.Chats.Select(c => c.ExternalId).ToListAsync(ct)).ToHashSet();
		List<SessionFolderItemEntity> orphans = await db.SessionFolderItems
			.Where(i => !chatIds.Contains(i.ChatExternalId))
			.ToListAsync(ct);

		if (orphans.Count == 0) return 0;

		db.SessionFolderItems.RemoveRange(orphans);
		await db.SaveChangesAsync(ct);
		return orphans.Count;
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<SessionFolderEntity> RequireAsync(string externalId, CancellationToken ct) =>
		await db.SessionFolders.FirstOrDefaultAsync(f => f.ExternalId == externalId, ct)
			?? throw new KeyNotFoundException($"Folder '{externalId}' not found");
}
