using Lis.Core.Util;

using LibGit2Sharp;

namespace Lis.Git.Services;

/// <summary>
/// Read/write Git operations backed by LibGit2Sharp. Pure .NET, no shell-out to the
/// system <c>git</c>. All paths are expected to be on the local filesystem and refer
/// to a git working tree (the main repo or a linked worktree).
/// </summary>
public interface IGitService {
	Task<RepoStatus>          GetRepoStatusAsync(string path, CancellationToken ct = default);
	Task<BranchListing>       ListBranchesAsync(string path, CancellationToken ct = default);
	Task<IReadOnlyList<WorktreeMetadata>> ListWorktreesAsync(string projectPath, CancellationToken ct = default);
	Task<WorktreeMetadata>    CreateWorktreeAsync(string projectPath, string newPath, string branch, CancellationToken ct = default);
	Task                      RemoveWorktreeAsync(string projectPath, string worktreePath, bool force = false, CancellationToken ct = default);

	/// <summary>
	/// Resolve the GitHub coordinates for a branch in a working tree by reading its
	/// upstream remote URL. Returns null when:
	///   - the path is not a git repo;
	///   - no remote is configured for the branch and there's no <c>origin</c> fallback;
	///   - the remote URL does not point at github.com.
	/// </summary>
	Task<RemoteRef?> GetRemoteRefAsync(string path, string branch, CancellationToken ct = default);

	/// <summary>
	/// Read the user's global git identity (<c>git config --global user.name/user.email/core.sshCommand</c>)
	/// from the system git config. Returns null when neither name nor email is set
	/// (treated as "no global identity configured").
	/// </summary>
	Task<GlobalGitIdentity?> GetGlobalIdentityAsync(CancellationToken ct = default);
}

/// <summary>Snapshot of a working tree's relationship to its upstream.</summary>
public sealed record RepoStatus(
	bool       IsGitRepo,
	string?    Branch,
	int        Ahead,
	int        Behind,
	bool       Dirty,
	CommitInfo? LastCommit
);

public sealed record CommitInfo(string Sha, string Message, string Author, DateTimeOffset At);

public sealed record BranchListing(
	IReadOnlyList<string> Local,
	IReadOnlyList<string> Remote,
	string?               Current
);

public sealed record WorktreeMetadata(
	string  Name,
	string  Path,
	string? Branch,
	string? Head,
	bool    IsLocked,
	bool    IsPrunable
);

/// <summary>Resolved GitHub coordinates for a branch's tracking remote.</summary>
public sealed record RemoteRef(string RemoteName, string Owner, string Repo, string Url);

/// <summary>
/// User's host-wide git identity from <c>git config --global</c>. Surfaced read-only.
/// </summary>
public sealed record GlobalGitIdentity(string? UserName, string? UserEmail, string? SshCommand);

public sealed class GitService :IGitService {

	[Trace("GitService > GetRepoStatusAsync")]
	public Task<RepoStatus> GetRepoStatusAsync(string path, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			return Task.FromResult(new RepoStatus(IsGitRepo: false, Branch: null, Ahead: 0, Behind: 0, Dirty: false, LastCommit: null));

		using Repository repo = new(path);
		Branch? head = repo.Head;
		Commit? tip  = head.Tip;
		bool dirty   = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false, IncludeUntracked = true }).IsDirty;

		int ahead  = head.TrackingDetails?.AheadBy  ?? 0;
		int behind = head.TrackingDetails?.BehindBy ?? 0;

		CommitInfo? last = tip is null
			? null
			: new CommitInfo(
				Sha:     tip.Sha,
				Message: tip.MessageShort ?? "",
				Author:  tip.Author?.Name ?? "",
				At:      tip.Author?.When ?? DateTimeOffset.MinValue
			);

		return Task.FromResult(new RepoStatus(
			IsGitRepo:  true,
			Branch:     head.FriendlyName,
			Ahead:      ahead,
			Behind:     behind,
			Dirty:      dirty,
			LastCommit: last
		));
	}

	[Trace("GitService > ListBranchesAsync")]
	public Task<BranchListing> ListBranchesAsync(string path, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			return Task.FromResult(new BranchListing(Local: [], Remote: [], Current: null));

		using Repository repo = new(path);

		List<string> local  = repo.Branches.Where(b => !b.IsRemote).Select(b => b.FriendlyName).OrderBy(n => n).ToList();
		List<string> remote = repo.Branches.Where(b =>  b.IsRemote).Select(b => b.FriendlyName).OrderBy(n => n).ToList();
		string? current     = repo.Head.FriendlyName;

		return Task.FromResult(new BranchListing(local, remote, current));
	}

	[Trace("GitService > ListWorktreesAsync")]
	public Task<IReadOnlyList<WorktreeMetadata>> ListWorktreesAsync(string projectPath, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(projectPath))
			return Task.FromResult<IReadOnlyList<WorktreeMetadata>>([]);

		using Repository repo = new(projectPath);
		List<WorktreeMetadata> result = [];

		// The primary working tree is not represented in repo.Worktrees — add it explicitly so
		// the sidebar shows it alongside any linked worktrees.
		result.Add(new WorktreeMetadata(
			Name:       "(main)",
			Path:       repo.Info.WorkingDirectory,
			Branch:     repo.Head.FriendlyName,
			Head:       repo.Head.Tip?.Sha,
			IsLocked:   false,
			IsPrunable: false
		));

		foreach (Worktree wt in repo.Worktrees) {
			Repository? wtRepo = null;
			try { wtRepo = wt.WorktreeRepository; } catch { /* prunable worktrees throw */ }

			result.Add(new WorktreeMetadata(
				Name:       wt.Name,
				Path:       wtRepo?.Info.WorkingDirectory ?? "",
				Branch:     wtRepo?.Head.FriendlyName,
				Head:       wtRepo?.Head.Tip?.Sha,
				IsLocked:   wt.IsLocked,
				IsPrunable: wtRepo is null
			));
			wtRepo?.Dispose();
		}

		return Task.FromResult<IReadOnlyList<WorktreeMetadata>>(result);
	}

	[Trace("GitService > CreateWorktreeAsync")]
	public Task<WorktreeMetadata> CreateWorktreeAsync(string projectPath, string newPath, string branch, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(projectPath))
			throw new ArgumentException($"'{projectPath}' is not a git repository", nameof(projectPath));

		using Repository repo = new(projectPath);

		// Derive a worktree name from the new path's leaf. Git uses this as the directory under .git/worktrees.
		string name = System.IO.Path.GetFileName(newPath.TrimEnd('/', '\\'));
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("newPath must end in a non-empty directory name", nameof(newPath));

		Branch? branchRef = repo.Branches[branch] ?? throw new ArgumentException($"Branch '{branch}' not found", nameof(branch));

		Worktree created = repo.Worktrees.Add(branchRef.FriendlyName, name, newPath, isLocked: false);

		using Repository wtRepo = created.WorktreeRepository;
		return Task.FromResult(new WorktreeMetadata(
			Name:       created.Name,
			Path:       wtRepo.Info.WorkingDirectory,
			Branch:     wtRepo.Head.FriendlyName,
			Head:       wtRepo.Head.Tip?.Sha,
			IsLocked:   created.IsLocked,
			IsPrunable: false
		));
	}

	[Trace("GitService > RemoveWorktreeAsync")]
	public Task RemoveWorktreeAsync(string projectPath, string worktreePath, bool force = false, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(projectPath))
			throw new ArgumentException($"'{projectPath}' is not a git repository", nameof(projectPath));

		using Repository repo = new(projectPath);
		Worktree? wt = repo.Worktrees.FirstOrDefault(w => {
			try { using Repository inner = w.WorktreeRepository; return PathsEquivalent(inner.Info.WorkingDirectory, worktreePath); }
			catch { return false; }
		});

		if (wt is null) throw new KeyNotFoundException($"Worktree at '{worktreePath}' not found");

		// Prune removes the .git/worktrees entry plus the working directory when allowed.
		repo.Worktrees.Prune(wt, ifLocked: force);
		return Task.CompletedTask;
	}

	[Trace("GitService > GetGlobalIdentityAsync")]
	public Task<GlobalGitIdentity?> GetGlobalIdentityAsync(CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();

		try {
			// BuildFrom(null) loads the global + xdg + system tiers — the same set
			// `git config --global` would write to. No working tree needed.
			using Configuration cfg = Configuration.BuildFrom(null);
			string? name = cfg.Get<string>("user.name")?.Value;
			string? email = cfg.Get<string>("user.email")?.Value;
			string? sshCommand = cfg.Get<string>("core.sshCommand")?.Value;

			if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email)) {
				return Task.FromResult<GlobalGitIdentity?>(null);
			}
			return Task.FromResult<GlobalGitIdentity?>(new GlobalGitIdentity(
				UserName:   string.IsNullOrWhiteSpace(name)       ? null : name,
				UserEmail:  string.IsNullOrWhiteSpace(email)      ? null : email,
				SshCommand: string.IsNullOrWhiteSpace(sshCommand) ? null : sshCommand
			));
		} catch (LibGit2SharpException) {
			// No config files on disk yet (fresh containers, sandboxed CI). Treat as no identity.
			return Task.FromResult<GlobalGitIdentity?>(null);
		}
	}

	[Trace("GitService > GetRemoteRefAsync")]
	public Task<RemoteRef?> GetRemoteRefAsync(string path, string branch, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path)) return Task.FromResult<RemoteRef?>(null);

		using Repository repo = new(path);

		// Prefer the branch's tracked remote; fall back to `origin` so detached branches still resolve.
		string? remoteName = null;
		if (!string.IsNullOrWhiteSpace(branch)) {
			Branch? local = repo.Branches[branch];
			if (local?.IsTracking == true && local.RemoteName is not null) remoteName = local.RemoteName;
		}
		remoteName ??= repo.Network.Remotes.Any(r => r.Name == "origin")
			? "origin"
			: repo.Network.Remotes.FirstOrDefault()?.Name;
		if (remoteName is null) return Task.FromResult<RemoteRef?>(null);

		Remote? remote = repo.Network.Remotes[remoteName];
		if (remote is null) return Task.FromResult<RemoteRef?>(null);

		(string owner, string repoName)? parsed = ParseGitHubUrl(remote.Url);
		if (parsed is null) return Task.FromResult<RemoteRef?>(null);

		return Task.FromResult<RemoteRef?>(new RemoteRef(
			RemoteName: remoteName,
			Owner:      parsed.Value.owner,
			Repo:       parsed.Value.repoName,
			Url:        remote.Url
		));
	}

	/// <summary>
	/// Best-effort parse of common GitHub remote URL shapes:
	///   https://github.com/owner/repo.git
	///   https://user:token@github.com/owner/repo.git
	///   git@github.com:owner/repo.git
	///   ssh://git@github.com/owner/repo
	/// Returns null for non-GitHub hosts.
	/// </summary>
	internal static (string owner, string repo)? ParseGitHubUrl(string url) {
		if (string.IsNullOrWhiteSpace(url)) return null;

		string normalised = url.Trim();
		// scp-style: git@github.com:owner/repo[.git]
		int colon = normalised.IndexOf(':');
		if (normalised.StartsWith("git@", StringComparison.OrdinalIgnoreCase) && colon > 0 && !normalised.Contains("://")) {
			string host = normalised[4..colon];
			if (!IsGitHubHost(host)) return null;
			string path = normalised[(colon + 1)..].Trim('/');
			return SplitOwnerRepo(path);
		}

		// URL-style (https / ssh)
		if (!Uri.TryCreate(normalised, UriKind.Absolute, out Uri? uri)) return null;
		if (!IsGitHubHost(uri.Host)) return null;
		return SplitOwnerRepo(uri.AbsolutePath.Trim('/'));
	}

	private static (string owner, string repo)? SplitOwnerRepo(string segment) {
		if (string.IsNullOrWhiteSpace(segment)) return null;
		string trimmed = segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
			? segment[..^4]
			: segment;
		string[] parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2) return null;
		return (parts[0], parts[1]);
	}

	private static bool IsGitHubHost(string host) =>
		string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
		host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);

	private static bool PathsEquivalent(string? a, string b) =>
		!string.IsNullOrEmpty(a) &&
		string.Equals(
			System.IO.Path.GetFullPath(a).TrimEnd('/', '\\'),
			System.IO.Path.GetFullPath(b).TrimEnd('/', '\\'),
			StringComparison.OrdinalIgnoreCase
		);
}
