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

	// ── Helpers ────────────────────────────────────────────────────

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
