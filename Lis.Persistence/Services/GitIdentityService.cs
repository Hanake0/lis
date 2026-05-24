using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence.Services;

/// <summary>
/// CRUD over user-defined <see cref="GitIdentityEntity"/> rows plus a default-identity
/// pointer stored in <see cref="SettingEntity"/>. Reading the actual <em>global</em>
/// git identity (whatever <c>git config --global user.name/email</c> is set to on the
/// host) is delegated to <c>Lis.Git.IGitService</c> in the Api layer so this project
/// stays free of LibGit2Sharp.
/// </summary>
public interface IGitIdentityService {
	Task<IReadOnlyList<GitIdentityEntity>> ListAsync(CancellationToken ct = default);
	Task<GitIdentityEntity?>               GetByExternalIdAsync(string externalId, CancellationToken ct = default);
	Task<GitIdentityEntity>                CreateAsync(GitIdentityEntity input, CancellationToken ct = default);
	Task<GitIdentityEntity>                UpdateAsync(string externalId, Action<GitIdentityEntity> mutate, CancellationToken ct = default);
	Task                                   DeleteAsync(string externalId, CancellationToken ct = default);

	Task<string?>                          GetDefaultIdentityExternalIdAsync(CancellationToken ct = default);
	Task                                   SetDefaultIdentityExternalIdAsync(string? externalId, CancellationToken ct = default);
}

public sealed class GitIdentityService(LisDbContext db, TimeProvider time) :IGitIdentityService {

	private const string DefaultIdentitySettingKey = "git.defaultIdentityExternalId";

	[Trace("GitIdentityService > ListAsync")]
	public async Task<IReadOnlyList<GitIdentityEntity>> ListAsync(CancellationToken ct = default) =>
		await db.GitIdentities.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(ct);

	[Trace("GitIdentityService > GetByExternalIdAsync")]
	public Task<GitIdentityEntity?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
		db.GitIdentities.FirstOrDefaultAsync(e => e.ExternalId == externalId, ct);

	[Trace("GitIdentityService > CreateAsync")]
	public async Task<GitIdentityEntity> CreateAsync(GitIdentityEntity input, CancellationToken ct = default) {
		Validate(input);

		DateTimeOffset now = time.GetUtcNow();
		GitIdentityEntity row = new() {
			ExternalId = string.IsNullOrWhiteSpace(input.ExternalId) ? NewExternalId() : input.ExternalId,
			Name       = input.Name.Trim(),
			UserName   = input.UserName.Trim(),
			UserEmail  = input.UserEmail.Trim(),
			AuthType   = NormaliseAuthType(input.AuthType),
			SshKeyPath = string.IsNullOrWhiteSpace(input.SshKeyPath) ? null : input.SshKeyPath.Trim(),
			Host       = string.IsNullOrWhiteSpace(input.Host)       ? null : input.Host.Trim(),
			Color      = string.IsNullOrWhiteSpace(input.Color)      ? null : input.Color.Trim(),
			Icon       = string.IsNullOrWhiteSpace(input.Icon)       ? null : input.Icon.Trim(),
			CreatedAt  = now,
			UpdatedAt  = now,
		};
		db.GitIdentities.Add(row);
		await db.SaveChangesAsync(ct);
		return row;
	}

	[Trace("GitIdentityService > UpdateAsync")]
	public async Task<GitIdentityEntity> UpdateAsync(string externalId, Action<GitIdentityEntity> mutate, CancellationToken ct = default) {
		GitIdentityEntity row = await this.RequireAsync(externalId, ct);
		mutate(row);
		Validate(row);
		row.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);
		return row;
	}

	[Trace("GitIdentityService > DeleteAsync")]
	public async Task DeleteAsync(string externalId, CancellationToken ct = default) {
		GitIdentityEntity row = await this.RequireAsync(externalId, ct);
		db.GitIdentities.Remove(row);

		// If the default identity was this one, clear the pointer so the UI shows the
		// global identity (or "no default") instead of pointing at a tombstone.
		SettingEntity? setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == DefaultIdentitySettingKey, ct);
		if (setting?.Value == externalId) {
			db.Settings.Remove(setting);
		}

		await db.SaveChangesAsync(ct);
	}

	[Trace("GitIdentityService > GetDefaultIdentityExternalIdAsync")]
	public async Task<string?> GetDefaultIdentityExternalIdAsync(CancellationToken ct = default) {
		SettingEntity? setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == DefaultIdentitySettingKey, ct);
		return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting.Value;
	}

	[Trace("GitIdentityService > SetDefaultIdentityExternalIdAsync")]
	public async Task SetDefaultIdentityExternalIdAsync(string? externalId, CancellationToken ct = default) {
		string? trimmed = externalId?.Trim();
		SettingEntity? setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == DefaultIdentitySettingKey, ct);

		if (string.IsNullOrEmpty(trimmed)) {
			if (setting is not null) db.Settings.Remove(setting);
			await db.SaveChangesAsync(ct);
			return;
		}

		DateTimeOffset now = time.GetUtcNow();
		if (setting is null) {
			db.Settings.Add(new SettingEntity { Key = DefaultIdentitySettingKey, Value = trimmed, UpdatedAt = now });
		} else {
			setting.Value     = trimmed;
			setting.UpdatedAt = now;
		}
		await db.SaveChangesAsync(ct);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<GitIdentityEntity> RequireAsync(string externalId, CancellationToken ct) =>
		await db.GitIdentities.FirstOrDefaultAsync(e => e.ExternalId == externalId, ct)
			?? throw new KeyNotFoundException($"Git identity '{externalId}' not found");

	private static void Validate(GitIdentityEntity row) {
		if (string.IsNullOrWhiteSpace(row.Name))      throw new ArgumentException("name is required");
		if (string.IsNullOrWhiteSpace(row.UserName))  throw new ArgumentException("user_name is required");
		if (string.IsNullOrWhiteSpace(row.UserEmail)) throw new ArgumentException("user_email is required");
		if (!row.UserEmail.Contains('@'))             throw new ArgumentException("user_email must be a valid address");
	}

	private static string NewExternalId() => Guid.NewGuid().ToString("N")[..16];

	private static string? NormaliseAuthType(string? t) {
		if (string.IsNullOrWhiteSpace(t)) return null;
		string s = t.Trim().ToLowerInvariant();
		return s is "ssh" or "token" ? s : throw new ArgumentException($"auth_type must be 'ssh' or 'token', got '{t}'");
	}
}
