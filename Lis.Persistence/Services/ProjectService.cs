using System.Security.Cryptography;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence.Services;

/// <summary>
/// Read/write operations for the <see cref="ProjectEntity"/> aggregate plus the
/// chat→project linkage. All methods return projects in <see cref="ProjectEntity.SortIndex"/>
/// then <see cref="ProjectEntity.CreatedAt"/> order so the sidebar can render directly
/// from the wire response without re-sorting.
/// </summary>
public interface IProjectService {
	Task<IReadOnlyList<ProjectEntity>>       ListAsync(CancellationToken ct = default);
	Task<ProjectEntity?>                     GetByExternalIdAsync(string externalId, CancellationToken ct = default);
	Task<ProjectEntity>                      CreateAsync(string path, string? label = null, string? color = null, CancellationToken ct = default);
	Task<ProjectEntity>                      UpdateMetaAsync(string externalId, ProjectMetaUpdate update, CancellationToken ct = default);
	Task<ProjectEntity>                      SetIconImageAsync(string externalId, string mime, byte[] blob, string source, CancellationToken ct = default);
	Task<ProjectEntity>                      RemoveIconAsync(string externalId, CancellationToken ct = default);
	Task                                     DeleteAsync(string externalId, CancellationToken ct = default);
	Task                                     ReorderAsync(IReadOnlyList<string> orderedExternalIds, CancellationToken ct = default);
	Task<ProjectEntity>                      TouchLastOpenedAsync(string externalId, CancellationToken ct = default);

	/// <summary>Reassign a chat's <c>project_id</c>. Pass null to detach. Throws if either side is missing.</summary>
	Task<ChatEntity> MoveChatToProjectAsync(string chatExternalId, string? projectExternalId, CancellationToken ct = default);
}

/// <summary>Subset-update DTO. Null fields mean "leave unchanged".</summary>
public sealed record ProjectMetaUpdate(
	string? Label            = null,
	string? Color            = null,
	string? Icon             = null,
	string? IconBackground   = null,
	bool?   SidebarCollapsed = null
);

public sealed class ProjectService(LisDbContext db, TimeProvider time) :IProjectService {
	// Curated palette matching OC's PROJECT_COLORS keys. Used by CreateAsync when no color is supplied.
	private static readonly string[] AutoColors =
		["blue", "purple", "green", "orange", "pink", "red", "yellow", "cyan", "teal", "indigo"];

	[Trace("ProjectService > ListAsync")]
	public async Task<IReadOnlyList<ProjectEntity>> ListAsync(CancellationToken ct = default) =>
		await db.Projects
				.AsNoTracking()
				.OrderBy(p => p.SortIndex)
				.ThenBy(p => p.CreatedAt)
				.ToListAsync(ct);

	[Trace("ProjectService > GetByExternalIdAsync")]
	public Task<ProjectEntity?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
		db.Projects.FirstOrDefaultAsync(p => p.ExternalId == externalId, ct);

	[Trace("ProjectService > CreateAsync")]
	public async Task<ProjectEntity> CreateAsync(string path, string? label = null, string? color = null, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));

		string normalizedPath = NormalizePath(path);
		string externalId     = HashPath(normalizedPath);

		// Reuse if a project with the same hash already exists — same semantics as OC.
		ProjectEntity? existing = await db.Projects.FirstOrDefaultAsync(p => p.ExternalId == externalId, ct);
		if (existing is not null) return existing;

		DateTimeOffset now = time.GetUtcNow();
		int maxSort = await db.Projects.AnyAsync(ct)
			? await db.Projects.MaxAsync(p => (int?)p.SortIndex, ct) ?? -1
			: -1;

		ProjectEntity project = new() {
			ExternalId = externalId,
			Path       = normalizedPath,
			Label      = string.IsNullOrWhiteSpace(label) ? DeriveLabel(normalizedPath) : label!.Trim(),
			Color      = color ?? await this.PickAutoColorAsync(ct),
			AddedAt    = now,
			CreatedAt  = now,
			UpdatedAt  = now,
			SortIndex  = maxSort + 1,
		};
		db.Projects.Add(project);
		await db.SaveChangesAsync(ct);
		return project;
	}

	[Trace("ProjectService > UpdateMetaAsync")]
	public async Task<ProjectEntity> UpdateMetaAsync(string externalId, ProjectMetaUpdate update, CancellationToken ct = default) {
		ProjectEntity project = await this.RequireAsync(externalId, ct);

		if (update.Label            is not null) project.Label            = update.Label;
		if (update.Color            is not null) project.Color            = update.Color;
		if (update.Icon             is not null) project.Icon             = update.Icon;
		if (update.IconBackground   is not null) project.IconBackground   = update.IconBackground;
		if (update.SidebarCollapsed is not null) project.SidebarCollapsed = update.SidebarCollapsed.Value;

		project.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return project;
	}

	[Trace("ProjectService > SetIconImageAsync")]
	public async Task<ProjectEntity> SetIconImageAsync(string externalId, string mime, byte[] blob, string source, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(mime))   throw new ArgumentException("mime is required", nameof(mime));
		if (blob.Length == 0)                  throw new ArgumentException("blob is empty",   nameof(blob));
		if (source is not ("custom" or "auto")) throw new ArgumentException("source must be 'custom' or 'auto'", nameof(source));

		ProjectEntity project = await this.RequireAsync(externalId, ct);
		project.IconImageMime      = mime;
		project.IconImageBlob      = blob;
		project.IconImageSource    = source;
		project.IconImageUpdatedAt = time.GetUtcNow();
		project.UpdatedAt          = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return project;
	}

	[Trace("ProjectService > RemoveIconAsync")]
	public async Task<ProjectEntity> RemoveIconAsync(string externalId, CancellationToken ct = default) {
		ProjectEntity project = await this.RequireAsync(externalId, ct);
		project.IconImageMime      = null;
		project.IconImageBlob      = null;
		project.IconImageSource    = null;
		project.IconImageUpdatedAt = null;
		project.UpdatedAt          = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return project;
	}

	[Trace("ProjectService > DeleteAsync")]
	public async Task DeleteAsync(string externalId, CancellationToken ct = default) {
		ProjectEntity project = await this.RequireAsync(externalId, ct);
		db.Projects.Remove(project);
		await db.SaveChangesAsync(ct);
	}

	[Trace("ProjectService > ReorderAsync")]
	public async Task ReorderAsync(IReadOnlyList<string> orderedExternalIds, CancellationToken ct = default) {
		if (orderedExternalIds.Count == 0) return;

		// Pull all referenced projects in one query, then assign sort_index by position.
		List<ProjectEntity> projects = await db.Projects
			.Where(p => orderedExternalIds.Contains(p.ExternalId))
			.ToListAsync(ct);

		DateTimeOffset now = time.GetUtcNow();
		for (int i = 0; i < orderedExternalIds.Count; i++) {
			ProjectEntity? p = projects.FirstOrDefault(x => x.ExternalId == orderedExternalIds[i]);
			if (p is null) continue;
			if (p.SortIndex != i) {
				p.SortIndex = i;
				p.UpdatedAt = now;
			}
		}

		await db.SaveChangesAsync(ct);
	}

	[Trace("ProjectService > TouchLastOpenedAsync")]
	public async Task<ProjectEntity> TouchLastOpenedAsync(string externalId, CancellationToken ct = default) {
		ProjectEntity project = await this.RequireAsync(externalId, ct);
		project.LastOpenedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return project;
	}

	[Trace("ProjectService > MoveChatToProjectAsync")]
	public async Task<ChatEntity> MoveChatToProjectAsync(string chatExternalId, string? projectExternalId, CancellationToken ct = default) {
		ChatEntity chat = await db.Chats.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId, ct)
			?? throw new KeyNotFoundException($"Chat '{chatExternalId}' not found");

		if (projectExternalId is null) {
			chat.ProjectId = null;
		} else {
			ProjectEntity project = await this.RequireAsync(projectExternalId, ct);
			chat.ProjectId = project.Id;
		}

		chat.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return chat;
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<ProjectEntity> RequireAsync(string externalId, CancellationToken ct) =>
		await db.Projects.FirstOrDefaultAsync(p => p.ExternalId == externalId, ct)
			?? throw new KeyNotFoundException($"Project '{externalId}' not found");

	private async Task<string> PickAutoColorAsync(CancellationToken ct) {
		// Count usage of each color; pick the least-used (random tiebreak).
		Dictionary<string, int> usage = AutoColors.ToDictionary(c => c, _ => 0);
		List<string?> existing = await db.Projects.Select(p => p.Color).ToListAsync(ct);
		foreach (string? c in existing) {
			if (c is not null && usage.TryGetValue(c, out int count)) usage[c] = count + 1;
		}
		int min = usage.Values.Min();
		List<string> candidates = usage.Where(kv => kv.Value == min).Select(kv => kv.Key).ToList();
		return candidates[Random.Shared.Next(candidates.Count)];
	}

	private static string NormalizePath(string raw) {
		string trimmed = raw.Trim().Replace('\\', '/');
		if (trimmed.Length > 1 && trimmed.EndsWith('/')) trimmed = trimmed.TrimEnd('/');
		return trimmed;
	}

	private static string HashPath(string normalizedPath) {
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
		// 16 chars from the hex digest — collision-safe for human-scale project counts.
		return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
	}

	private static string DeriveLabel(string normalizedPath) {
		if (string.IsNullOrEmpty(normalizedPath) || normalizedPath == "/") return "Root";
		string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		string raw = segments.Length > 0 ? segments[^1] : normalizedPath;
		// Replace -_ with space and title-case each word.
		string spaced = raw.Replace('-', ' ').Replace('_', ' ');
		return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
		                              .Select(w => char.ToUpper(w[0]) + w[1..]));
	}
}
