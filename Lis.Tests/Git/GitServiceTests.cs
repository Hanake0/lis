using Lis.Git.Services;

using LibGit2Sharp;

namespace Lis.Tests.Git;

/// <summary>
/// Integration-style tests against a real temp git repo seeded with LibGit2Sharp.
/// Each test instance gets its own temp dir and disposes it on teardown.
/// </summary>
public sealed class GitServiceTests :IDisposable {
	private readonly string     repoDir;
	private readonly GitService sut = new();

	public GitServiceTests() {
		this.repoDir = Path.Combine(Path.GetTempPath(), $"lis-git-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(this.repoDir);
	}

	public void Dispose() {
		// Best-effort cleanup; on Windows libgit2 may leave file handles for a tick.
		try { ForceDeleteDirectory(this.repoDir); } catch { /* ignore */ }
		GC.SuppressFinalize(this);
	}

	private static void ForceDeleteDirectory(string path) {
		if (!Directory.Exists(path)) return;
		foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
			File.SetAttributes(f, FileAttributes.Normal);
		Directory.Delete(path, recursive: true);
	}

	// ── GetRepoStatusAsync ─────────────────────────────────────────

	[Fact]
	public async Task GetRepoStatusAsync_Returns_NotARepo_When_Empty_Dir() {
		RepoStatus status = await this.sut.GetRepoStatusAsync(this.repoDir);
		Assert.False(status.IsGitRepo);
		Assert.Null(status.Branch);
		Assert.Null(status.LastCommit);
	}

	[Fact]
	public async Task GetRepoStatusAsync_Returns_Branch_And_LastCommit_When_Initialised() {
		(string head, _) = SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		RepoStatus status = await this.sut.GetRepoStatusAsync(this.repoDir);

		Assert.True(status.IsGitRepo);
		Assert.NotNull(status.Branch);
		Assert.NotNull(status.LastCommit);
		Assert.Equal(head, status.LastCommit!.Sha);
		Assert.Equal("init", status.LastCommit.Message);
		Assert.False(status.Dirty);
		Assert.Equal(0, status.Ahead);
		Assert.Equal(0, status.Behind);
	}

	[Fact]
	public async Task GetRepoStatusAsync_Reports_Dirty_When_Untracked_File_Exists() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await File.WriteAllTextAsync(Path.Combine(this.repoDir, "new.txt"), "x");

		RepoStatus status = await this.sut.GetRepoStatusAsync(this.repoDir);
		Assert.True(status.Dirty);
	}

	// ── ListBranchesAsync ──────────────────────────────────────────

	[Fact]
	public async Task ListBranchesAsync_Returns_Empty_When_Not_A_Repo() {
		BranchListing list = await this.sut.ListBranchesAsync(this.repoDir);
		Assert.Empty(list.Local);
		Assert.Empty(list.Remote);
		Assert.Null(list.Current);
	}

	[Fact]
	public async Task ListBranchesAsync_Returns_Local_Branches_And_Current() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		using (Repository repo = new(this.repoDir)) {
			repo.CreateBranch("feature/x");
			repo.CreateBranch("feature/y");
		}

		BranchListing list = await this.sut.ListBranchesAsync(this.repoDir);

		Assert.Contains("feature/x", list.Local);
		Assert.Contains("feature/y", list.Local);
		Assert.NotNull(list.Current);
	}

	// ── Worktrees ──────────────────────────────────────────────────

	[Fact]
	public async Task ListWorktreesAsync_Returns_Main_When_No_Linked_Worktrees() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		IReadOnlyList<WorktreeMetadata> wts = await this.sut.ListWorktreesAsync(this.repoDir);

		WorktreeMetadata main = Assert.Single(wts);
		Assert.Equal("(main)", main.Name);
		Assert.False(main.IsPrunable);
		Assert.False(main.IsLocked);
		Assert.NotNull(main.Branch);
	}

	[Fact]
	public async Task ListWorktreesAsync_NonRepo_Returns_Empty() {
		IReadOnlyList<WorktreeMetadata> wts = await this.sut.ListWorktreesAsync(this.repoDir);
		Assert.Empty(wts);
	}

	[Fact]
	public async Task CreateWorktreeAsync_Throws_When_Not_A_Repo() {
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.CreateWorktreeAsync(this.repoDir, Path.Combine(this.repoDir, "wt"), "main"));
	}

	[Fact]
	public async Task CreateWorktreeAsync_Throws_When_Branch_Missing() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.CreateWorktreeAsync(this.repoDir, Path.Combine(this.repoDir, "..", "wt-x"), "no-such-branch"));
	}

	[Fact]
	public async Task RemoveWorktreeAsync_Throws_When_Not_A_Repo() {
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.RemoveWorktreeAsync(this.repoDir, Path.Combine(this.repoDir, "wt")));
	}

	[Fact]
	public async Task RemoveWorktreeAsync_Throws_When_Worktree_Missing() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.RemoveWorktreeAsync(this.repoDir, Path.Combine(this.repoDir, "ghost")));
	}

	[Fact]
	public async Task CreateWorktreeAsync_Then_List_Shows_Linked_Worktree() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		using (Repository repo = new(this.repoDir)) repo.CreateBranch("feature");

		string wtPath = Path.Combine(Path.GetTempPath(), $"lis-git-wt-{Guid.NewGuid():N}");
		try {
			WorktreeMetadata wt = await this.sut.CreateWorktreeAsync(this.repoDir, wtPath, "feature");
			Assert.False(wt.IsPrunable);
			Assert.Contains("feature", wt.Branch);

			IReadOnlyList<WorktreeMetadata> list = await this.sut.ListWorktreesAsync(this.repoDir);
			Assert.Equal(2, list.Count); // main + linked
			Assert.Contains(list, w => w.Branch is not null && w.Branch.Contains("feature"));
		} finally {
			try { ForceDeleteDirectory(wtPath); } catch { /* ignore */ }
		}
	}

	// ── GetRemoteRefAsync ──────────────────────────────────────────

	[Fact]
	public async Task GetRemoteRefAsync_Returns_Null_When_Not_A_Repo() {
		RemoteRef? r = await this.sut.GetRemoteRefAsync(this.repoDir, "main");
		Assert.Null(r);
	}

	[Fact]
	public async Task GetRemoteRefAsync_Returns_Null_When_No_Remotes() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		RemoteRef? r = await this.sut.GetRemoteRefAsync(this.repoDir, "main");
		Assert.Null(r);
	}

	[Fact]
	public async Task GetRemoteRefAsync_Resolves_Origin_For_Detached_Branch() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		using (Repository repo = new(this.repoDir)) {
			repo.Network.Remotes.Add("origin", "https://github.com/octocat/Hello-World.git");
		}

		RemoteRef? r = await this.sut.GetRemoteRefAsync(this.repoDir, "non-tracking-branch");

		Assert.NotNull(r);
		Assert.Equal("origin",       r!.RemoteName);
		Assert.Equal("octocat",      r.Owner);
		Assert.Equal("Hello-World",  r.Repo);
	}

	[Fact]
	public async Task GetRemoteRefAsync_Returns_Null_For_Non_GitHub_Remote() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		using (Repository repo = new(this.repoDir)) {
			repo.Network.Remotes.Add("origin", "https://gitlab.com/octocat/Hello-World.git");
		}

		RemoteRef? r = await this.sut.GetRemoteRefAsync(this.repoDir, "main");
		Assert.Null(r);
	}

	// ── ParseGitHubUrl ─────────────────────────────────────────────

	[Theory]
	[InlineData("https://github.com/owner/repo.git",                  "owner", "repo")]
	[InlineData("https://github.com/owner/repo",                      "owner", "repo")]
	[InlineData("https://user:token@github.com/owner/repo.git",       "owner", "repo")]
	[InlineData("git@github.com:owner/repo.git",                      "owner", "repo")]
	[InlineData("git@github.com:owner/repo",                          "owner", "repo")]
	[InlineData("ssh://git@github.com/owner/repo.git",                "owner", "repo")]
	[InlineData("https://api.github.com/repos/owner/repo",            "repos", "owner")] // accepts any path with ≥2 segments; caller's responsibility to feed remote URLs
	public void ParseGitHubUrl_Parses_Common_Shapes(string url, string expectedOwner, string expectedRepo) {
		(string owner, string repo)? parsed = GitService.ParseGitHubUrl(url);
		Assert.NotNull(parsed);
		Assert.Equal(expectedOwner, parsed!.Value.owner);
		Assert.Equal(expectedRepo,  parsed.Value.repo);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not a url")]
	[InlineData("https://gitlab.com/owner/repo.git")]
	[InlineData("git@bitbucket.org:owner/repo.git")]
	[InlineData("https://github.com/owner")] // only one segment
	public void ParseGitHubUrl_Rejects_Non_GitHub_Or_Incomplete(string url) {
		Assert.Null(GitService.ParseGitHubUrl(url));
	}

	// ── CreateBranchAsync / RenameBranchAsync / DeleteBranchAsync ──

	[Fact]
	public async Task CreateBranchAsync_From_HEAD_When_StartPoint_Null() {
		(string head, _) = SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		BranchRef created = await this.sut.CreateBranchAsync(this.repoDir, "feature/x", startPoint: null);

		Assert.Equal("feature/x", created.Name);
		Assert.Equal(head,        created.Tip);
	}

	[Fact]
	public async Task CreateBranchAsync_Resolves_StartPoint_As_Branch_Or_Sha() {
		(string head, _) = SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		using (Repository repo = new(this.repoDir)) repo.CreateBranch("base");

		BranchRef fromBranch = await this.sut.CreateBranchAsync(this.repoDir, "from-branch", "base");
		BranchRef fromSha    = await this.sut.CreateBranchAsync(this.repoDir, "from-sha",    head);

		Assert.Equal(head, fromBranch.Tip);
		Assert.Equal(head, fromSha.Tip);
	}

	[Fact]
	public async Task CreateBranchAsync_Throws_When_Branch_Already_Exists() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await this.sut.CreateBranchAsync(this.repoDir, "dup", null);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			this.sut.CreateBranchAsync(this.repoDir, "dup", null));
	}

	[Fact]
	public async Task RenameBranchAsync_Renames_And_Returns_New_Ref() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await this.sut.CreateBranchAsync(this.repoDir, "old-name", null);

		BranchRef renamed = await this.sut.RenameBranchAsync(this.repoDir, "old-name", "new-name");

		Assert.Equal("new-name", renamed.Name);
		using Repository repo = new(this.repoDir);
		Assert.Null   (repo.Branches["old-name"]);
		Assert.NotNull(repo.Branches["new-name"]);
	}

	[Fact]
	public async Task RenameBranchAsync_Throws_When_NewName_Exists() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await this.sut.CreateBranchAsync(this.repoDir, "a", null);
		await this.sut.CreateBranchAsync(this.repoDir, "b", null);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			this.sut.RenameBranchAsync(this.repoDir, "a", "b"));
	}

	[Fact]
	public async Task DeleteBranchAsync_Removes_Local_Branch() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await this.sut.CreateBranchAsync(this.repoDir, "tmp", null);

		await this.sut.DeleteBranchAsync(this.repoDir, "tmp", force: false);

		using Repository repo = new(this.repoDir);
		Assert.Null(repo.Branches["tmp"]);
	}

	[Fact]
	public async Task DeleteBranchAsync_Throws_On_Current_Branch() {
		(_, string current) = SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			this.sut.DeleteBranchAsync(this.repoDir, current, force: false));
	}

	[Fact]
	public async Task DeleteBranchAsync_Throws_Not_Fully_Merged_When_Branch_Has_Unique_Commit() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		// Add a commit on a side branch so it diverges from HEAD.
		using (Repository repo = new(this.repoDir)) {
			string defaultBranch = GetDefaultBranchName(repo);
			Branch side = repo.CreateBranch("side");
			Commands.Checkout(repo, side);
			#pragma warning disable CA1849 // sync inside using-block keeps the test linear
			File.WriteAllText(Path.Combine(this.repoDir, "side.txt"), "side");
			#pragma warning restore CA1849
			Commands.Stage(repo, "side.txt");
			Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
			repo.Commit("side commit", sig, sig);
			Commands.Checkout(repo, repo.Branches[defaultBranch]);
		}

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			this.sut.DeleteBranchAsync(this.repoDir, "side", force: false));
		Assert.Contains("not fully merged", ex.Message);
	}

	[Fact]
	public async Task DeleteBranchAsync_Forces_Through_Unmerged_When_Force_True() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		using (Repository repo = new(this.repoDir)) {
			string defaultBranch = GetDefaultBranchName(repo);
			Branch side = repo.CreateBranch("side");
			Commands.Checkout(repo, side);
			#pragma warning disable CA1849 // sync inside using-block keeps the test linear
			File.WriteAllText(Path.Combine(this.repoDir, "side.txt"), "side");
			#pragma warning restore CA1849
			Commands.Stage(repo, "side.txt");
			Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
			repo.Commit("side commit", sig, sig);
			Commands.Checkout(repo, repo.Branches[defaultBranch]);
		}

		await this.sut.DeleteBranchAsync(this.repoDir, "side", force: true);

		using Repository repo2 = new(this.repoDir);
		Assert.Null(repo2.Branches["side"]);
	}

	// ── Stash ─────────────────────────────────────────────────────

	[Fact]
	public async Task SaveStashAsync_Returns_Created_False_On_Clean_Tree() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		StashSaveResult result = await this.sut.SaveStashAsync(this.repoDir, message: null, includeUntracked: false);

		Assert.False(result.Created);
		Assert.Null (result.Ref);
	}

	[Fact]
	public async Task SaveStashAsync_Then_ListStashes_Returns_One_Entry() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await File.WriteAllTextAsync(Path.Combine(this.repoDir, "README.md"), "updated");

		StashSaveResult save = await this.sut.SaveStashAsync(this.repoDir, message: "wip", includeUntracked: false);

		Assert.True(save.Created);
		Assert.Equal("stash@{0}", save.Ref);

		IReadOnlyList<StashEntryInfo> list = await this.sut.ListStashesAsync(this.repoDir);
		StashEntryInfo entry = Assert.Single(list);
		Assert.Equal(0,          entry.Index);
		Assert.Equal("stash@{0}", entry.Ref);
		Assert.Contains("wip",    entry.Message);
	}

	[Fact]
	public async Task ApplyStashAsync_Restores_Working_Tree_Changes() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		string file = Path.Combine(this.repoDir, "README.md");
		await File.WriteAllTextAsync(file, "modified");

		await this.sut.SaveStashAsync(this.repoDir, message: null, includeUntracked: false);
		Assert.Equal("hello", await File.ReadAllTextAsync(file)); // stash reverted

		await this.sut.ApplyStashAsync(this.repoDir, "stash@{0}");

		Assert.Equal("modified", await File.ReadAllTextAsync(file));
		// Apply doesn't drop, so the entry should still be listed.
		Assert.Single(await this.sut.ListStashesAsync(this.repoDir));
	}

	[Fact]
	public async Task PopStashAsync_Restores_And_Removes() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await File.WriteAllTextAsync(Path.Combine(this.repoDir, "README.md"), "modified");
		await this.sut.SaveStashAsync(this.repoDir, null, false);

		await this.sut.PopStashAsync(this.repoDir, "stash@{0}");

		Assert.Empty(await this.sut.ListStashesAsync(this.repoDir));
	}

	[Fact]
	public async Task DropStashAsync_Removes_Without_Applying() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		string file = Path.Combine(this.repoDir, "README.md");
		await File.WriteAllTextAsync(file, "modified");
		await this.sut.SaveStashAsync(this.repoDir, null, false);

		await this.sut.DropStashAsync(this.repoDir, "stash@{0}");

		Assert.Empty(await this.sut.ListStashesAsync(this.repoDir));
		Assert.Equal("hello", await File.ReadAllTextAsync(file)); // unchanged
	}

	[Fact]
	public async Task CountStashFilesAsync_Counts_Files_Per_Ref() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await File.WriteAllTextAsync(Path.Combine(this.repoDir, "README.md"), "x");
		await File.WriteAllTextAsync(Path.Combine(this.repoDir, "a.txt"),     "a");
		// "a.txt" is untracked, so we need includeUntracked for the count to include it.
		await this.sut.SaveStashAsync(this.repoDir, null, includeUntracked: true);

		IReadOnlyDictionary<string, int> counts = await this.sut.CountStashFilesAsync(
			this.repoDir, ["stash@{0}", "stash@{9}"]);

		Assert.Equal(2, counts["stash@{0}"]); // README.md + a.txt
		Assert.Equal(0, counts["stash@{9}"]); // out of range → 0
	}

	[Fact]
	public async Task ApplyStashAsync_Throws_On_Out_Of_Range_Ref() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.ApplyStashAsync(this.repoDir, "stash@{5}"));
	}

	// ── GetCommitFileDiffAsync ────────────────────────────────────

	[Fact]
	public async Task GetCommitFileDiffAsync_Returns_Original_And_Modified() {
		(_, _) = SeedRepoWithCommit(this.repoDir, "code.cs", "v1", "first");
		string sha2;
		using (Repository repo = new(this.repoDir)) {
			#pragma warning disable CA1849
			File.WriteAllText(Path.Combine(this.repoDir, "code.cs"), "v2");
			#pragma warning restore CA1849
			Commands.Stage(repo, "code.cs");
			Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
			sha2 = repo.Commit("second", sig, sig).Sha;
		}

		CommitFileDiff diff = await this.sut.GetCommitFileDiffAsync(this.repoDir, sha2, "code.cs");

		Assert.Equal("v1", diff.Original);
		Assert.Equal("v2", diff.Modified);
		Assert.False(diff.IsBinary);
	}

	[Fact]
	public async Task GetCommitFileDiffAsync_Throws_For_Missing_Commit() {
		SeedRepoWithCommit(this.repoDir, "code.cs", "v1", "first");
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.GetCommitFileDiffAsync(this.repoDir, "0000000000000000000000000000000000000000", "code.cs"));
	}

	// ── GetConflictDetailsAsync ───────────────────────────────────

	[Fact]
	public async Task GetConflictDetailsAsync_Returns_Null_When_Clean() {
		SeedRepoWithCommit(this.repoDir, "README.md", "hello", "init");

		MergeConflictDetails? d = await this.sut.GetConflictDetailsAsync(this.repoDir);

		Assert.Null(d);
	}

	[Fact]
	public async Task GetConflictDetailsAsync_Detects_Active_Merge_With_Conflicts() {
		SeedRepoWithCommit(this.repoDir, "file.txt", "base", "init");

		using (Repository repo = new(this.repoDir)) {
			string defaultBranch = GetDefaultBranchName(repo);

			// Branch A: change file → "A"
			Branch a = repo.CreateBranch("branch-a");
			Commands.Checkout(repo, a);
			#pragma warning disable CA1849
			File.WriteAllText(Path.Combine(this.repoDir, "file.txt"), "A");
			#pragma warning restore CA1849
			Commands.Stage(repo, "file.txt");
			Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
			repo.Commit("change-a", sig, sig);

			// Branch B (from base): change same file → "B"
			Commands.Checkout(repo, repo.Branches[defaultBranch]);
			Branch b = repo.CreateBranch("branch-b");
			Commands.Checkout(repo, b);
			#pragma warning disable CA1849
			File.WriteAllText(Path.Combine(this.repoDir, "file.txt"), "B");
			#pragma warning restore CA1849
			Commands.Stage(repo, "file.txt");
			repo.Commit("change-b", sig, sig);

			// On branch-b, merge branch-a → produces conflict on file.txt
			MergeResult result = repo.Merge(repo.Branches["branch-a"], sig);
			Assert.Equal(MergeStatus.Conflicts, result.Status);
		}

		MergeConflictDetails? d = await this.sut.GetConflictDetailsAsync(this.repoDir);

		Assert.NotNull(d);
		Assert.Equal("merge", d!.Operation);
		Assert.Contains("file.txt", d.UnmergedFiles);
		Assert.False(string.IsNullOrEmpty(d.StatusPorcelain));
	}

	// ── Worktree bootstrap sidecar ────────────────────────────────

	[Fact]
	public async Task GetWorktreeBootstrapStatusAsync_Returns_Ready_When_No_Sidecar() {
		WorktreeBootstrapStatus s = await this.sut.GetWorktreeBootstrapStatusAsync(this.repoDir);
		Assert.Equal("ready", s.Status);
		Assert.Null (s.Error);
	}

	[Fact]
	public async Task SetWorktreeBootstrapStatus_Then_Get_Roundtrips() {
		WorktreeBootstrapStatus seed = new("pending", null, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		await this.sut.SetWorktreeBootstrapStatusAsync(this.repoDir, seed);

		WorktreeBootstrapStatus read = await this.sut.GetWorktreeBootstrapStatusAsync(this.repoDir);
		Assert.Equal("pending", read.Status);
		Assert.Equal(seed.UpdatedAt, read.UpdatedAt);
	}

	[Fact]
	public async Task SetWorktreeBootstrapStatus_Throws_When_Worktree_Dir_Missing() {
		string ghost = Path.Combine(Path.GetTempPath(), $"lis-git-ghost-{Guid.NewGuid():N}");
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.SetWorktreeBootstrapStatusAsync(ghost, new WorktreeBootstrapStatus("ready", null, DateTimeOffset.UtcNow)));
	}

	// ── Helpers ────────────────────────────────────────────────────

	private static string GetDefaultBranchName(Repository repo) {
		// LibGit2Sharp's `Repository.Init` defaults to "master" on older libgit2, "main" on newer.
		// Just use whatever the current HEAD reports.
		return repo.Head.FriendlyName;
	}

	private static (string head, string branch) SeedRepoWithCommit(string dir, string fileName, string content, string message) {
		Repository.Init(dir);
		File.WriteAllText(Path.Combine(dir, fileName), content);

		using Repository repo = new(dir);
		Commands.Stage(repo, fileName);
		Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
		Commit commit = repo.Commit(message, sig, sig);
		return (commit.Sha, repo.Head.FriendlyName);
	}
}
