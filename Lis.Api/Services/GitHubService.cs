using System.Collections.Concurrent;

using Lis.Core.Util;
using Lis.Git.Services;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Octokit;

namespace Lis.Api.Services;

/// <summary>
/// GitHub PAT management plus enriched PR-status lookup. Tokens are stored encrypted via
/// <see cref="IDataProtectionProvider"/> and never returned to clients. PR status is cached
/// in-memory for 60 s so repeated sidebar polls don't blow the GitHub API rate limit.
///
/// The PR-status path supports two resolution modes:
///   1. Explicit <c>{owner, repo, branch}</c> — for tooling that already knows the coordinates.
///   2. <c>{directory, branch}</c> — the sidebar's common case; the backend resolves the
///      GitHub coordinates via <see cref="IGitService.GetRemoteRefAsync"/>.
/// </summary>
public interface IGitHubService {
	Task<GitHubStatus>           GetStatusAsync(CancellationToken ct = default);
	Task<GitHubStatus>           SetTokenAsync(string token, CancellationToken ct = default);
	Task                         ClearTokenAsync(CancellationToken ct = default);

	Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusAsync(IReadOnlyList<PrKey> keys, CancellationToken ct = default);

	/// <summary>
	/// Resolve directory+branch to GitHub coordinates, then fetch enriched PR status.
	/// Per-target failures (non-GitHub remote, missing PR, GitHub API hiccup) become a
	/// <see cref="PrStatus.Disconnected"/> entry rather than throwing — the sidebar
	/// queries many targets per render and one bad repo should not poison the batch.
	/// </summary>
	Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusByDirectoryAsync(IReadOnlyList<DirectoryPrKey> targets, CancellationToken ct = default);
}

public sealed record GitHubStatus(bool Connected, string? Login, string? Scopes);

public sealed record PrKey(string Owner, string Repo, string Branch) {
	public string Cache => $"{this.Owner}/{this.Repo}@{this.Branch}";
}

public sealed record DirectoryPrKey(string Directory, string Branch) {
	public string Cache => $"{this.Directory}::{this.Branch}";
}

// ── Enriched PR shape ───────────────────────────────────────────────────────

/// <summary>
/// Snapshot of a GitHub PR for a directory+branch. Matches the OC consumer's
/// <c>GitHubPullRequestStatus</c> contract so byte-identical sidebar/git-view code
/// keeps rendering. All metadata is optional — when <see cref="Connected"/> is
/// false, the entry surfaces "no token / not a github remote / no PR" and the UI
/// renders its empty state.
/// </summary>
public sealed record PrStatus(
	bool                Connected,
	PrRepoRef?          Repo,
	string?             Branch,
	PullRequestDetail?  Pr,
	ChecksSummary?      Checks,
	bool?               CanMerge,
	string?             DefaultBranch,
	string?             ResolvedRemoteName,
	string?             Error
) {
	public static PrStatus Disconnected(string? error = null) =>
		new(Connected: false, Repo: null, Branch: null, Pr: null, Checks: null,
			CanMerge: null, DefaultBranch: null, ResolvedRemoteName: null, Error: error);
}

public sealed record PrRepoRef(string Owner, string Repo);

public sealed record PullRequestDetail(
	int     Number,
	string  Title,
	string? Body,
	string  Url,
	string  State,    // open | closed | merged
	bool    Draft,
	string  Base,
	string  Head,
	string? HeadSha,
	bool?   Mergeable,
	string? MergeableState
);

public sealed record ChecksSummary(
	string? State,    // success | failure | pending | neutral | null
	int     Total,
	int     Pending,
	int     Success,
	int     Failure
);

// ── Implementation ──────────────────────────────────────────────────────────

public sealed class GitHubService(
	LisDbContext            db,
	IDataProtectionProvider dataProtection,
	IGitService             git,
	TimeProvider            time
) :IGitHubService {

	// Purpose string scopes the protector — never change it or existing tokens go dark.
	private readonly IDataProtector protector = dataProtection.CreateProtector("Lis.GitHub.Token.v1");

	// 60 s TTL cache shared across all PR lookups. Keyed by `owner/repo@branch` so directory-
	// based queries that resolve to the same repo reuse the cached PR.
	private static readonly ConcurrentDictionary<string, CachedPr> Cache    = new();
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

		GitHubClient? client = await this.TryCreateClientAsync(ct);
		if (client is null) {
			// No token → return Disconnected for every key. The sidebar uses this to hide badges.
			return keys.ToDictionary(k => k.Cache, _ => PrStatus.Disconnected());
		}

		Dictionary<string, PrStatus> result = [];
		DateTimeOffset now = time.GetUtcNow();
		foreach (PrKey key in keys) {
			if (Cache.TryGetValue(key.Cache, out CachedPr? cached) && cached.ExpiresAt > now) {
				result[key.Cache] = cached.Status;
				continue;
			}

			PrStatus status = await FetchEnrichedAsync(client, key.Owner, key.Repo, key.Branch, remoteName: null, ct);
			Cache[key.Cache] = new CachedPr(status, now + CacheTtl);
			result[key.Cache] = status;
		}
		return result;
	}

	[Trace("GitHubService > GetPrStatusByDirectoryAsync")]
	public async Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusByDirectoryAsync(IReadOnlyList<DirectoryPrKey> targets, CancellationToken ct = default) {
		if (targets.Count == 0) return new Dictionary<string, PrStatus>();

		GitHubClient? client = await this.TryCreateClientAsync(ct);
		Dictionary<string, PrStatus> result = [];
		DateTimeOffset now = time.GetUtcNow();

		foreach (DirectoryPrKey target in targets) {
			RemoteRef? remote;
			try { remote = await git.GetRemoteRefAsync(target.Directory, target.Branch, ct); }
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)               { result[target.Cache] = PrStatus.Disconnected(ex.Message); continue; }

			if (remote is null) {
				// Either not a git repo, no remote configured, or remote isn't GitHub.
				result[target.Cache] = PrStatus.Disconnected();
				continue;
			}

			string cacheKey = new PrKey(remote.Owner, remote.Repo, target.Branch).Cache;
			if (Cache.TryGetValue(cacheKey, out CachedPr? cached) && cached.ExpiresAt > now) {
				// Re-stamp resolvedRemoteName so the client sees the directory-specific resolution.
				result[target.Cache] = cached.Status with { ResolvedRemoteName = remote.RemoteName };
				continue;
			}

			if (client is null) {
				// We resolved the remote but have no token. Tell the client about the resolution
				// so the UI can offer a "connect GitHub" prompt with the right repo context.
				PrStatus disconnected = PrStatus.Disconnected() with {
					Repo               = new PrRepoRef(remote.Owner, remote.Repo),
					Branch             = target.Branch,
					ResolvedRemoteName = remote.RemoteName,
				};
				result[target.Cache] = disconnected;
				continue;
			}

			PrStatus status = await FetchEnrichedAsync(client, remote.Owner, remote.Repo, target.Branch, remote.RemoteName, ct);
			Cache[cacheKey]      = new CachedPr(status, now + CacheTtl);
			result[target.Cache] = status;
		}
		return result;
	}

	// ── Internals ──────────────────────────────────────────────────

	private async Task<GitHubClient?> TryCreateClientAsync(CancellationToken ct) {
		GitHubIdentityEntity? identity = await db.GitHubIdentities.AsNoTracking().FirstOrDefaultAsync(ct);
		if (identity is null) return null;
		string token = this.protector.Unprotect(identity.TokenProtected);
		return new GitHubClient(new ProductHeaderValue("Lis")) { Credentials = new Credentials(token) };
	}

	private static async Task<PrStatus> FetchEnrichedAsync(GitHubClient client, string owner, string repo, string branch, string? remoteName, CancellationToken ct) {
		try {
			// Default branch first — cheap and we need it for "canMerge" and base hints.
			Repository? repoMeta = null;
			try { repoMeta = await client.Repository.Get(owner, repo); }
			catch (NotFoundException) { /* repo invisible to this token */ }

			PullRequestRequest req = new() { State = ItemStateFilter.Open, Head = $"{owner}:{branch}" };
			IReadOnlyList<PullRequest> prs = await client.PullRequest.GetAllForRepository(owner, repo, req);
			ct.ThrowIfCancellationRequested();

			PullRequest? pr = prs.Count == 0 ? null : prs[0];
			if (pr is null) {
				return new PrStatus(
					Connected:          true,
					Repo:               new PrRepoRef(owner, repo),
					Branch:             branch,
					Pr:                 null,
					Checks:             null,
					CanMerge:           null,
					DefaultBranch:      repoMeta?.DefaultBranch,
					ResolvedRemoteName: remoteName,
					Error:              null
				);
			}

			ChecksSummary? checks = pr.Head?.Sha is null ? null : await FetchChecksAsync(client, owner, repo, pr.Head.Sha, ct);

			string state = pr.Merged ? "merged" : pr.State.Value.ToString().ToLowerInvariant();
			return new PrStatus(
				Connected:          true,
				Repo:               new PrRepoRef(owner, repo),
				Branch:             branch,
				Pr:                 new PullRequestDetail(
					Number:         pr.Number,
					Title:          pr.Title ?? "",
					Body:           pr.Body,
					Url:            pr.HtmlUrl,
					State:          state,
					Draft:          pr.Draft,
					Base:           pr.Base?.Ref ?? "",
					Head:           pr.Head?.Ref ?? branch,
					HeadSha:        pr.Head?.Sha,
					Mergeable:      pr.Mergeable,
					MergeableState: pr.MergeableState?.StringValue
				),
				Checks:             checks,
				CanMerge:           pr.Mergeable == true && checks?.State != "failure",
				DefaultBranch:      repoMeta?.DefaultBranch,
				ResolvedRemoteName: remoteName,
				Error:              null
			);
		} catch (NotFoundException) {
			return new PrStatus(
				Connected:          true,
				Repo:               new PrRepoRef(owner, repo),
				Branch:             branch,
				Pr:                 null,
				Checks:             null,
				CanMerge:           null,
				DefaultBranch:      null,
				ResolvedRemoteName: remoteName,
				Error:              null
			);
		} catch (RateLimitExceededException ex) {
			return PrStatus.Disconnected($"GitHub rate limit exceeded. Reset at {ex.Reset:HH:mm}.") with {
				Repo               = new PrRepoRef(owner, repo),
				Branch             = branch,
				ResolvedRemoteName = remoteName,
			};
		} catch (ApiException ex) {
			return PrStatus.Disconnected(ex.Message) with {
				Repo               = new PrRepoRef(owner, repo),
				Branch             = branch,
				ResolvedRemoteName = remoteName,
			};
		}
	}

	/// <summary>
	/// Fetch the combined status + check runs for a head SHA and roll them into a single
	/// <see cref="ChecksSummary"/>. Combined-status alone misses GitHub Actions runs;
	/// check-runs alone misses legacy status APIs. We merge both for an accurate count.
	/// </summary>
	private static async Task<ChecksSummary?> FetchChecksAsync(GitHubClient client, string owner, string repo, string sha, CancellationToken ct) {
		int success = 0, pending = 0, failure = 0;

		try {
			CombinedCommitStatus combined = await client.Repository.Status.GetCombined(owner, repo, sha);
			ct.ThrowIfCancellationRequested();
			foreach (CommitStatus st in combined.Statuses) {
				CommitState commitState = st.State.Value;
				switch (commitState) {
					case CommitState.Success: success++; break;
					case CommitState.Pending: pending++; break;
					case CommitState.Failure:
					case CommitState.Error:   failure++; break;
				}
			}
		} catch (ApiException) { /* repo without status checks → leave counters at 0 */ }

		try {
			CheckRunsResponse runs = await client.Check.Run.GetAllForReference(owner, repo, sha);
			ct.ThrowIfCancellationRequested();
			foreach (CheckRun run in runs.CheckRuns) {
				if (run.Status.Value != CheckStatus.Completed) { pending++; continue; }
				switch (run.Conclusion?.Value) {
					case CheckConclusion.Success:
					case CheckConclusion.Neutral:
					case CheckConclusion.Skipped: success++; break;
					case CheckConclusion.Failure:
					case CheckConclusion.TimedOut:
					case CheckConclusion.Cancelled:
					case CheckConclusion.ActionRequired:
					case CheckConclusion.Stale:    failure++; break;
					default:                        pending++; break;
				}
			}
		} catch (ApiException) { /* checks API unavailable → use what we already have */ }

		int total = success + pending + failure;
		if (total == 0) return null;

		string? state = failure > 0 ? "failure"
			: pending > 0           ? "pending"
			: success > 0           ? "success"
			:                          null;

		return new ChecksSummary(State: state, Total: total, Pending: pending, Success: success, Failure: failure);
	}

	private sealed record CachedPr(PrStatus Status, DateTimeOffset ExpiresAt);

	/// <summary>For tests: drop the static cache so each test sees fresh fetches.</summary>
	internal static void ResetCacheForTesting() => Cache.Clear();
}
