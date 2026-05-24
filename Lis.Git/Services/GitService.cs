using System.Text;

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

	// ── Branch CRUD ────────────────────────────────────────────────

	/// <summary>Create a local branch pointing at <paramref name="startPoint"/> (or HEAD when omitted).</summary>
	Task<BranchRef> CreateBranchAsync(string path, string name, string? startPoint, CancellationToken ct = default);

	/// <summary>
	/// Delete a local branch. When <paramref name="force"/> is false and the branch
	/// is not merged into HEAD, throws an <see cref="InvalidOperationException"/> with
	/// "not fully merged" in the message — the dialog catches this to prompt the user
	/// for a force-delete.
	/// </summary>
	Task DeleteBranchAsync(string path, string name, bool force, CancellationToken ct = default);

	/// <summary>Rename a local branch. Throws when <paramref name="oldName"/> doesn't exist or <paramref name="newName"/> already does.</summary>
	Task<BranchRef> RenameBranchAsync(string path, string oldName, string newName, CancellationToken ct = default);

	// ── Stash ──────────────────────────────────────────────────────

	/// <summary>List stashes in stash@{N} order (newest first).</summary>
	Task<IReadOnlyList<StashEntryInfo>> ListStashesAsync(string path, CancellationToken ct = default);

	/// <summary>Save current working tree changes as a new stash. Returns null when the working tree was clean.</summary>
	Task<StashSaveResult> SaveStashAsync(string path, string? message, bool includeUntracked, CancellationToken ct = default);

	/// <summary>Apply a stash (by ref like <c>stash@{0}</c> or zero-based index) without removing it.</summary>
	Task ApplyStashAsync(string path, string stashRef, CancellationToken ct = default);

	/// <summary>Apply + drop in one operation.</summary>
	Task PopStashAsync(string path, string stashRef, CancellationToken ct = default);

	/// <summary>Drop a stash without applying.</summary>
	Task DropStashAsync(string path, string stashRef, CancellationToken ct = default);

	/// <summary>Map <c>stash@{N}</c> refs to the number of files changed in that stash. Missing refs map to 0.</summary>
	Task<IReadOnlyDictionary<string, int>> CountStashFilesAsync(string path, IReadOnlyList<string> refs, CancellationToken ct = default);

	// ── Diff / conflict / commit inspection ────────────────────────

	/// <summary>
	/// Per-file diff for a commit — returns the file content before and after the change so
	/// the front-end Monaco diff viewer can render them side by side. Includes an
	/// <c>IsBinary</c> flag so the caller skips rendering for non-text content.
	/// </summary>
	Task<CommitFileDiff> GetCommitFileDiffAsync(string path, string commitSha, string filePath, CancellationToken ct = default);

	/// <summary>
	/// Snapshot the current merge/rebase conflict state. Returns null when the working tree is clean
	/// (no MERGE_HEAD / REBASE_HEAD). The payload mirrors what a developer would assemble by running
	/// <c>git status --porcelain</c>, <c>git diff</c>, and <c>cat .git/MERGE_HEAD</c>.
	/// </summary>
	Task<MergeConflictDetails?> GetConflictDetailsAsync(string path, CancellationToken ct = default);

	// ── Worktree bootstrap ─────────────────────────────────────────

	/// <summary>
	/// Read the bootstrap status sidecar (<c>.lis-bootstrap.json</c>) at the worktree root.
	/// Used by NewWorktreeDialog to surface "setup commands running…" progress.
	/// When the file is missing the worktree is considered <c>ready</c>.
	/// </summary>
	Task<WorktreeBootstrapStatus> GetWorktreeBootstrapStatusAsync(string worktreePath, CancellationToken ct = default);

	/// <summary>Write the bootstrap status sidecar (overwrites any existing content).</summary>
	Task SetWorktreeBootstrapStatusAsync(string worktreePath, WorktreeBootstrapStatus status, CancellationToken ct = default);

	// ── Existing endpoints ─────────────────────────────────────────

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

/// <summary>Lightweight branch reference returned by branch CRUD operations.</summary>
public sealed record BranchRef(string Name, string Tip);

/// <summary>One entry in <c>git stash list</c>.</summary>
public sealed record StashEntryInfo(
	int            Index,
	string         Ref,           // "stash@{N}"
	string         Hash,          // stash commit SHA
	string         Message,
	DateTimeOffset At
);

/// <summary>
/// Outcome of <see cref="IGitService.SaveStashAsync"/>. <c>Created</c> is false when the
/// working tree was clean (so the front-end can render "nothing to stash").
/// </summary>
public sealed record StashSaveResult(bool Created, string? Ref, string Message);

/// <summary>Side-by-side text payload + binary flag for the Monaco diff viewer.</summary>
public sealed record CommitFileDiff(string Original, string Modified, bool IsBinary);

/// <summary>
/// Snapshot of a merge/rebase mid-conflict, structured exactly the way OC's ConflictDialog expects.
/// All free-form strings are pre-formatted as the dialog would otherwise format them itself.
/// </summary>
public sealed record MergeConflictDetails(
	string                Operation,        // "merge" | "rebase"
	string                StatusPorcelain,  // `git status --porcelain` output
	IReadOnlyList<string> UnmergedFiles,
	string                Diff,             // unified diff of conflicts
	string?               HeadInfo          // MERGE_HEAD / REBASE_HEAD contents (sha + subject)
);

/// <summary>Bootstrap progress for a freshly-created worktree (setup commands running).</summary>
public sealed record WorktreeBootstrapStatus(
	string         Status,    // "pending" | "ready" | "failed"
	string?        Error,
	DateTimeOffset UpdatedAt
);

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

	// ── Branch CRUD ────────────────────────────────────────────────

	[Trace("GitService > CreateBranchAsync")]
	public Task<BranchRef> CreateBranchAsync(string path, string name, string? startPoint, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("branch name is required", nameof(name));
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		if (repo.Branches[name] is not null)
			throw new InvalidOperationException($"Branch '{name}' already exists");

		// Resolve the start point. Empty → HEAD. Otherwise look up as branch first, then as sha/ref.
		Commit target;
		if (string.IsNullOrWhiteSpace(startPoint)) {
			target = repo.Head.Tip
				?? throw new InvalidOperationException("Cannot create a branch from an unborn HEAD");
		} else {
			Branch? branch = repo.Branches[startPoint];
			if (branch?.Tip is not null) {
				target = branch.Tip;
			} else {
				GitObject? obj = repo.Lookup(startPoint);
				target = obj?.Peel<Commit>()
					?? throw new ArgumentException($"start point '{startPoint}' not found", nameof(startPoint));
			}
		}

		Branch created = repo.CreateBranch(name, target);
		return Task.FromResult(new BranchRef(created.FriendlyName, created.Tip!.Sha));
	}

	[Trace("GitService > DeleteBranchAsync")]
	public Task DeleteBranchAsync(string path, string name, bool force, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("branch name is required", nameof(name));
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		Branch branch = repo.Branches[name]
			?? throw new KeyNotFoundException($"Branch '{name}' not found");

		if (branch.IsCurrentRepositoryHead)
			throw new InvalidOperationException("Cannot delete the currently checked-out branch");

		// libgit2 won't refuse on "not fully merged" — we have to enforce it ourselves so
		// callers get the same UX as `git branch -d`. The dialog parses "not fully merged"
		// out of the exception message to prompt the user for a force-delete.
		if (!force) {
			Commit? tip  = branch.Tip;
			Commit? head = repo.Head.Tip;
			if (tip is not null && head is not null) {
				bool merged = repo.ObjectDatabase.FindMergeBase(tip, head)?.Sha == tip.Sha;
				if (!merged)
					throw new InvalidOperationException($"Branch '{name}' is not fully merged into HEAD");
			}
		}

		repo.Branches.Remove(branch);
		return Task.CompletedTask;
	}

	[Trace("GitService > RenameBranchAsync")]
	public Task<BranchRef> RenameBranchAsync(string path, string oldName, string newName, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(oldName)) throw new ArgumentException("oldName is required", nameof(oldName));
		if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("newName is required", nameof(newName));
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		Branch source = repo.Branches[oldName]
			?? throw new KeyNotFoundException($"Branch '{oldName}' not found");
		if (repo.Branches[newName] is not null)
			throw new InvalidOperationException($"Branch '{newName}' already exists");

		Branch renamed = repo.Branches.Rename(source, newName, allowOverwrite: false);
		return Task.FromResult(new BranchRef(renamed.FriendlyName, renamed.Tip!.Sha));
	}

	// ── Stash ──────────────────────────────────────────────────────

	[Trace("GitService > ListStashesAsync")]
	public Task<IReadOnlyList<StashEntryInfo>> ListStashesAsync(string path, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			return Task.FromResult<IReadOnlyList<StashEntryInfo>>([]);

		using Repository repo = new(path);
		List<StashEntryInfo> result = [];
		int i = 0;
		foreach (Stash stash in repo.Stashes) {
			Commit work = stash.WorkTree;
			result.Add(new StashEntryInfo(
				Index:   i,
				Ref:     $"stash@{{{i}}}",
				Hash:    work.Sha,
				Message: work.MessageShort ?? stash.Message ?? "",
				At:      work.Committer?.When ?? DateTimeOffset.MinValue
			));
			i++;
		}
		return Task.FromResult<IReadOnlyList<StashEntryInfo>>(result);
	}

	[Trace("GitService > SaveStashAsync")]
	public Task<StashSaveResult> SaveStashAsync(string path, string? message, bool includeUntracked, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		Signature stasher = BuildStashSignature(repo);
		StashModifiers mods = includeUntracked
			? StashModifiers.IncludeUntracked
			: StashModifiers.Default;

		Stash? created = repo.Stashes.Add(stasher, message, mods);
		if (created is null)
			return Task.FromResult(new StashSaveResult(Created: false, Ref: null, Message: "no changes to stash"));

		return Task.FromResult(new StashSaveResult(
			Created: true,
			Ref:     "stash@{0}",
			Message: created.WorkTree.MessageShort ?? message ?? ""
		));
	}

	[Trace("GitService > ApplyStashAsync")]
	public Task ApplyStashAsync(string path, string stashRef, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		int idx = ResolveStashIndex(repo, stashRef);
		StashApplyStatus status = repo.Stashes.Apply(idx);
		if (status == StashApplyStatus.Conflicts)
			throw new InvalidOperationException("Stash applied with conflicts; resolve them before continuing");
		if (status != StashApplyStatus.Applied)
			throw new InvalidOperationException($"Failed to apply stash {stashRef}: {status}");

		return Task.CompletedTask;
	}

	[Trace("GitService > PopStashAsync")]
	public Task PopStashAsync(string path, string stashRef, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		int idx = ResolveStashIndex(repo, stashRef);
		StashApplyStatus status = repo.Stashes.Pop(idx);
		if (status == StashApplyStatus.Conflicts)
			throw new InvalidOperationException("Stash popped with conflicts; the stash entry was not removed");
		if (status != StashApplyStatus.Applied)
			throw new InvalidOperationException($"Failed to pop stash {stashRef}: {status}");

		return Task.CompletedTask;
	}

	[Trace("GitService > DropStashAsync")]
	public Task DropStashAsync(string path, string stashRef, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));

		using Repository repo = new(path);
		int idx = ResolveStashIndex(repo, stashRef);
		repo.Stashes.Remove(idx);
		return Task.CompletedTask;
	}

	[Trace("GitService > CountStashFilesAsync")]
	public Task<IReadOnlyDictionary<string, int>> CountStashFilesAsync(string path, IReadOnlyList<string> refs, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		Dictionary<string, int> result = [];
		if (!Repository.IsValid(path) || refs.Count == 0)
			return Task.FromResult<IReadOnlyDictionary<string, int>>(result);

		using Repository repo = new(path);
		List<Stash> stashes = repo.Stashes.ToList();

		foreach (string stashRef in refs) {
			if (!TryParseStashIndex(stashRef, out int idx) || idx < 0 || idx >= stashes.Count) {
				result[stashRef] = 0;
				continue;
			}

			Stash stash      = stashes[idx];
			Commit baseTree  = stash.Base;
			HashSet<string> paths = [];

			// Tracked working-tree changes: WorkTree-vs-Base.
			TreeChanges work = repo.Diff.Compare<TreeChanges>(baseTree.Tree, stash.WorkTree.Tree);
			foreach (TreeEntryChanges change in work) paths.Add(change.Path);

			// Untracked files (only present when stash was saved with IncludeUntracked):
			// libgit2 represents them as a separate parent commit on the stash.
			Commit? untracked = stash.Untracked;
			if (untracked is not null) {
				TreeChanges untrackedDiff = repo.Diff.Compare<TreeChanges>(baseTree.Tree, untracked.Tree);
				foreach (TreeEntryChanges change in untrackedDiff) paths.Add(change.Path);
			}

			result[stashRef] = paths.Count;
		}

		return Task.FromResult<IReadOnlyDictionary<string, int>>(result);
	}

	private static Signature BuildStashSignature(Repository repo) {
		Configuration cfg = repo.Config;
		string name  = cfg.Get<string>("user.name")?.Value  ?? Environment.UserName ?? "lis";
		string email = cfg.Get<string>("user.email")?.Value ?? $"{name}@lis";
		return new Signature(name, email, DateTimeOffset.UtcNow);
	}

	private static int ResolveStashIndex(Repository repo, string stashRef) {
		if (!TryParseStashIndex(stashRef, out int idx))
			throw new ArgumentException($"invalid stash ref '{stashRef}' (expected 'stash@{{N}}' or 'N')", nameof(stashRef));
		int count = repo.Stashes.Count();
		if (idx < 0 || idx >= count)
			throw new KeyNotFoundException($"stash {stashRef} not found (have {count})");
		return idx;
	}

	private static bool TryParseStashIndex(string stashRef, out int index) {
		// Accepts "stash@{N}", "N", and the bare form "stash@N" (some shells eat braces).
		if (int.TryParse(stashRef, out index)) return true;
		if (stashRef.StartsWith("stash@{", StringComparison.Ordinal) && stashRef.EndsWith('}')) {
			ReadOnlySpan<char> inner = stashRef.AsSpan("stash@{".Length, stashRef.Length - "stash@{".Length - 1);
			return int.TryParse(inner, out index);
		}
		if (stashRef.StartsWith("stash@", StringComparison.Ordinal)) {
			return int.TryParse(stashRef.AsSpan("stash@".Length), out index);
		}
		index = -1;
		return false;
	}

	// ── Diff / conflict ────────────────────────────────────────────

	[Trace("GitService > GetCommitFileDiffAsync")]
	public Task<CommitFileDiff> GetCommitFileDiffAsync(string path, string commitSha, string filePath, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path))
			throw new ArgumentException($"'{path}' is not a git repository", nameof(path));
		if (string.IsNullOrWhiteSpace(commitSha)) throw new ArgumentException("commitSha is required", nameof(commitSha));
		if (string.IsNullOrWhiteSpace(filePath))  throw new ArgumentException("filePath is required",  nameof(filePath));

		using Repository repo = new(path);
		Commit? commit = repo.Lookup<Commit>(commitSha)
			?? throw new KeyNotFoundException($"commit '{commitSha}' not found");
		Commit? parent = commit.Parents.FirstOrDefault();

		// "original" = the file as it was BEFORE this commit (parent side).
		// "modified" = the file as it is AT this commit.
		Blob? originalBlob = parent is null ? null : FindBlob(parent, filePath);
		Blob? modifiedBlob = FindBlob(commit, filePath);

		bool isBinary = (originalBlob?.IsBinary ?? false) || (modifiedBlob?.IsBinary ?? false);
		string original = isBinary || originalBlob is null ? "" : ReadBlobUtf8(originalBlob);
		string modified = isBinary || modifiedBlob is null ? "" : ReadBlobUtf8(modifiedBlob);

		return Task.FromResult(new CommitFileDiff(original, modified, isBinary));
	}

	private static Blob? FindBlob(Commit commit, string filePath) {
		// Walk the tree to find the blob. LibGit2Sharp uses `/` separators internally.
		string normalised = filePath.Replace('\\', '/');
		TreeEntry? entry  = commit.Tree[normalised];
		return entry?.Target as Blob;
	}

	private static string ReadBlobUtf8(Blob blob) {
		using Stream stream = blob.GetContentStream();
		using StreamReader reader = new(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}

	[Trace("GitService > GetConflictDetailsAsync")]
	public async Task<MergeConflictDetails?> GetConflictDetailsAsync(string path, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!Repository.IsValid(path)) return null;

		using Repository repo = new(path);
		CurrentOperation op = repo.Info.CurrentOperation;
		string? operation = op switch {
			CurrentOperation.Merge             => "merge",
			CurrentOperation.Rebase            => "rebase",
			CurrentOperation.RebaseInteractive => "rebase",
			CurrentOperation.RebaseMerge       => "rebase",
			CurrentOperation.CherryPick        => "cherry-pick",
			CurrentOperation.Revert            => "revert",
			_                                  => null,
		};
		if (operation is null) return null;

		// Build the porcelain view + unmerged file list. We don't shell out to git; instead use
		// the per-file status with the IsConflicted flag.
		StatusOptions statusOpts = new() {
			DetectRenamesInIndex    = false,
			DetectRenamesInWorkDir  = false,
			IncludeUntracked        = true,
		};
		RepositoryStatus status   = repo.RetrieveStatus(statusOpts);
		StringBuilder porcelain   = new();
		List<string>  unmerged    = [];
		foreach (StatusEntry entry in status) {
			string code = PorcelainCode(entry.State);
			porcelain.Append(code).Append(' ').Append(entry.FilePath).Append('\n');
			if ((entry.State & FileStatus.Conflicted) == FileStatus.Conflicted)
				unmerged.Add(entry.FilePath);
		}

		// Unified diff of the working tree against the index — that's what `git diff` shows.
		Patch patch = repo.Diff.Compare<Patch>(repo.Head?.Tip?.Tree, DiffTargets.WorkingDirectory);
		string diff = patch.Content;

		// HEAD info: sha + subject line of MERGE_HEAD / REBASE_HEAD (or just the rebase progress).
		string? headInfo = null;
		string  gitDir   = repo.Info.Path; // ends with "/.git/"
		string  marker   = operation == "merge" ? "MERGE_HEAD" : "REBASE_HEAD";
		string  markerPath = System.IO.Path.Combine(gitDir, marker);
		if (File.Exists(markerPath)) {
			string sha = (await File.ReadAllTextAsync(markerPath, ct)).Trim();
			Commit? c = repo.Lookup<Commit>(sha);
			headInfo  = c is null ? sha : $"{sha}  {c.MessageShort}";
		}

		return new MergeConflictDetails(
			Operation:       operation,
			StatusPorcelain: porcelain.ToString(),
			UnmergedFiles:   unmerged,
			Diff:            diff,
			HeadInfo:        headInfo
		);
	}

	private static string PorcelainCode(FileStatus state) {
		if ((state & FileStatus.Conflicted)     == FileStatus.Conflicted)     return "UU";
		if ((state & FileStatus.NewInIndex)     == FileStatus.NewInIndex)     return "A ";
		if ((state & FileStatus.NewInWorkdir)   == FileStatus.NewInWorkdir)   return "??";
		if ((state & FileStatus.DeletedFromIndex)   == FileStatus.DeletedFromIndex)   return "D ";
		if ((state & FileStatus.DeletedFromWorkdir) == FileStatus.DeletedFromWorkdir) return " D";
		if ((state & FileStatus.ModifiedInIndex)    == FileStatus.ModifiedInIndex)    return "M ";
		if ((state & FileStatus.ModifiedInWorkdir)  == FileStatus.ModifiedInWorkdir)  return " M";
		return "??";
	}

	// ── Worktree bootstrap ─────────────────────────────────────────

	private const string BootstrapSidecarFile = ".lis-bootstrap.json";

	[Trace("GitService > GetWorktreeBootstrapStatusAsync")]
	public async Task<WorktreeBootstrapStatus> GetWorktreeBootstrapStatusAsync(string worktreePath, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(worktreePath))
			throw new ArgumentException("worktreePath is required", nameof(worktreePath));

		string file = System.IO.Path.Combine(worktreePath, BootstrapSidecarFile);
		if (!File.Exists(file)) {
			// No sidecar — treat as ready. The dialog stops polling once it sees this.
			return new WorktreeBootstrapStatus("ready", null, DateTimeOffset.MinValue);
		}

		await using FileStream fs = File.OpenRead(file);
		WorktreeBootstrapStatus? parsed = await System.Text.Json.JsonSerializer.DeserializeAsync<WorktreeBootstrapStatus>(fs, cancellationToken: ct);
		return parsed ?? new WorktreeBootstrapStatus("ready", null, DateTimeOffset.MinValue);
	}

	[Trace("GitService > SetWorktreeBootstrapStatusAsync")]
	public async Task SetWorktreeBootstrapStatusAsync(string worktreePath, WorktreeBootstrapStatus status, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(worktreePath))
			throw new ArgumentException("worktreePath is required", nameof(worktreePath));
		if (!Directory.Exists(worktreePath))
			throw new KeyNotFoundException($"worktree '{worktreePath}' does not exist");

		string file = System.IO.Path.Combine(worktreePath, BootstrapSidecarFile);
		await using FileStream fs = File.Create(file);
		await System.Text.Json.JsonSerializer.SerializeAsync(fs, status, cancellationToken: ct);
	}

	// ── Existing endpoints ─────────────────────────────────────────

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
