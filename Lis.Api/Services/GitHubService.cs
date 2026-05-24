using System.Collections.Concurrent;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Octokit;

namespace Lis.Api.Services;

/// <summary>
/// GitHub PAT management plus PR-status lookup. Tokens are stored encrypted via
/// <see cref="IDataProtectionProvider"/> and never returned to clients. PR status
/// is cached in-memory for 60 s so repeated sidebar queries don't blow the GitHub
/// rate limit.
/// </summary>
public interface IGitHubService {
	Task<GitHubStatus>           GetStatusAsync(CancellationToken ct = default);
	Task<GitHubStatus>           SetTokenAsync(string token, CancellationToken ct = default);
	Task                         ClearTokenAsync(CancellationToken ct = default);
	Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusAsync(IReadOnlyList<PrKey> keys, CancellationToken ct = default);
}

public sealed record GitHubStatus(bool Connected, string? Login, string? Scopes);

public sealed record PrKey(string Owner, string Repo, string Branch) {
	public string Cache => $"{this.Owner}/{this.Repo}@{this.Branch}";
}

public sealed record PrStatus(string? State, string? Checks, string? Reviews);

public sealed class GitHubService(
	LisDbContext            db,
	IDataProtectionProvider dataProtection,
	TimeProvider            time
) :IGitHubService {

	// Purpose string scopes the protector — never change it or existing tokens go dark.
	private readonly IDataProtector protector = dataProtection.CreateProtector("Lis.GitHub.Token.v1");

	// 60 s TTL cache shared across all PR lookups.
	private static readonly ConcurrentDictionary<string, CachedPr> Cache = new();
	private static readonly TimeSpan                                CacheTtl = TimeSpan.FromSeconds(60);

	[Trace("GitHubService > GetStatusAsync")]
	public async Task<GitHubStatus> GetStatusAsync(CancellationToken ct = default) {
		GitHubIdentityEntity? id = await db.GitHubIdentities.AsNoTracking().FirstOrDefaultAsync(ct);
		return id is null
			? new GitHubStatus(Connected: false, Login: null, Scopes: null)
			: new GitHubStatus(Connected: true,  Login: id.Login, Scopes: id.Scopes);
	}

	[Trace("GitHubService > SetTokenAsync")]
	public async Task<GitHubStatus> SetTokenAsync(string token, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

		// Validate the token by asking GitHub who owns it. Any failure here is an Argument error,
		// not a 500 — bad input from the user.
		GitHubClient client = new(new ProductHeaderValue("Lis")) { Credentials = new Credentials(token) };
		User user;
		try { user = await client.User.Current(); }
		catch (AuthorizationException ex) { throw new ArgumentException("GitHub rejected the token", nameof(token), ex); }
		catch (ApiException ex)           { throw new ArgumentException($"GitHub rejected the token: {ex.Message}", nameof(token), ex); }

		// Token scopes ship in the X-OAuth-Scopes response header; parse from the last response.
		string? scopes = client.GetLastApiInfo()?.OauthScopes is { Count: > 0 } s ? string.Join(",", s) : null;

		DateTimeOffset now = time.GetUtcNow();
		GitHubIdentityEntity? existing = await db.GitHubIdentities.FirstOrDefaultAsync(ct);
		if (existing is null) {
			db.GitHubIdentities.Add(new GitHubIdentityEntity {
				Login          = user.Login,
				TokenProtected = this.protector.Protect(token),
				Scopes         = scopes,
				AddedAt        = now,
				UpdatedAt      = now,
			});
		} else {
			existing.Login          = user.Login;
			existing.TokenProtected = this.protector.Protect(token);
			existing.Scopes         = scopes;
			existing.UpdatedAt      = now;
		}
		await db.SaveChangesAsync(ct);
		Cache.Clear(); // identity changed → drop stale PR cache so future queries use the new token

		return new GitHubStatus(Connected: true, Login: user.Login, Scopes: scopes);
	}

	[Trace("GitHubService > ClearTokenAsync")]
	public async Task ClearTokenAsync(CancellationToken ct = default) {
		List<GitHubIdentityEntity> all = await db.GitHubIdentities.ToListAsync(ct);
		if (all.Count == 0) return;
		db.GitHubIdentities.RemoveRange(all);
		await db.SaveChangesAsync(ct);
		Cache.Clear();
	}

	[Trace("GitHubService > GetPrStatusAsync")]
	public async Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusAsync(IReadOnlyList<PrKey> keys, CancellationToken ct = default) {
		if (keys.Count == 0) return new Dictionary<string, PrStatus>();

		GitHubIdentityEntity? identity = await db.GitHubIdentities.AsNoTracking().FirstOrDefaultAsync(ct);
		// No identity yet → degrade gracefully with empty results instead of throwing; the sidebar
		// uses this to decide whether to render PR badges.
		if (identity is null) return new Dictionary<string, PrStatus>();

		string token = this.protector.Unprotect(identity.TokenProtected);
		GitHubClient client = new(new ProductHeaderValue("Lis")) { Credentials = new Credentials(token) };

		Dictionary<string, PrStatus> result = [];
		DateTimeOffset now = time.GetUtcNow();
		foreach (PrKey key in keys) {
			if (Cache.TryGetValue(key.Cache, out CachedPr? cached) && cached.ExpiresAt > now) {
				result[key.Cache] = cached.Status;
				continue;
			}

			PrStatus status = await FetchOneAsync(client, key, ct);
			Cache[key.Cache]   = new CachedPr(status, now + CacheTtl);
			result[key.Cache] = status;
		}
		return result;
	}

	private static async Task<PrStatus> FetchOneAsync(GitHubClient client, PrKey key, CancellationToken ct) {
		try {
			PullRequestRequest req = new() { State = ItemStateFilter.Open, Head = $"{key.Owner}:{key.Branch}" };
			IReadOnlyList<PullRequest> prs = await client.PullRequest.GetAllForRepository(key.Owner, key.Repo, req);
			ct.ThrowIfCancellationRequested();

			PullRequest? pr = prs.Count == 0 ? null : prs[0];
			if (pr is null) return new PrStatus(State: null, Checks: null, Reviews: null);

			return new PrStatus(
				State:   pr.State.Value.ToString().ToLowerInvariant(),
				Checks:  null,  // detailed checks/reviews require extra calls — leave for follow-up
				Reviews: null
			);
		} catch (NotFoundException) {
			return new PrStatus(State: null, Checks: null, Reviews: null);
		}
	}

	private sealed record CachedPr(PrStatus Status, DateTimeOffset ExpiresAt);
}
