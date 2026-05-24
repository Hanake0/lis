using Lis.Api.Services;
using Lis.Git.Services;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="GitHubService"/>. Live GitHub calls (SetTokenAsync's
/// validation hop, GetPrStatusAsync's PR query) are out of scope here — those
/// require a real token and network. We cover everything else: status with /
/// without identity, clear-token, token encryption round-trip, and pr-status
/// degraded path (no identity → empty results).
/// </summary>
public sealed class GitHubServiceTests :IDisposable {
	private readonly LisDbContext                    db;
	private readonly FakeTimeProvider                time;
	private readonly EphemeralDataProtectionProvider dataProtection;
	private readonly FakeGitService                  git;
	private readonly GitHubService                   sut;

	public GitHubServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"github-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db             = new TestDbContext(options);
		this.time           = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.dataProtection = new EphemeralDataProtectionProvider();
		this.git            = new FakeGitService();
		this.sut            = new GitHubService(this.db, this.dataProtection, this.git, this.time);
		// Static PR cache is shared across tests — wipe it so each test starts fresh.
		GitHubService.ResetCacheForTesting();
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	[Fact]
	public async Task GetStatusAsync_NoIdentity_Returns_Disconnected() {
		GitHubStatus status = await this.sut.GetStatusAsync();
		Assert.False(status.Connected);
		Assert.Null(status.Login);
	}

	[Fact]
	public async Task GetStatusAsync_With_Existing_Identity_Returns_Login_And_Scopes() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		this.db.GitHubIdentities.Add(new GitHubIdentityEntity {
			Login          = "octocat",
			TokenProtected = protector.Protect("ghp_xxx"),
			Scopes         = "repo,read:user",
			AddedAt        = this.time.GetUtcNow(),
			UpdatedAt      = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();

		GitHubStatus status = await this.sut.GetStatusAsync();

		Assert.True(status.Connected);
		Assert.Equal("octocat",         status.Login);
		Assert.Equal("repo,read:user",  status.Scopes);
	}

	[Fact]
	public async Task ClearTokenAsync_Removes_All_Identities() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		this.db.GitHubIdentities.Add(new GitHubIdentityEntity {
			Login          = "octocat",
			TokenProtected = protector.Protect("ghp_xxx"),
			AddedAt        = this.time.GetUtcNow(),
			UpdatedAt      = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();

		await this.sut.ClearTokenAsync();

		Assert.Empty(await this.db.GitHubIdentities.ToListAsync());
	}

	[Fact]
	public async Task ClearTokenAsync_NoIdentity_Is_NoOp() {
		await this.sut.ClearTokenAsync(); // should not throw
	}

	[Fact]
	public async Task SetTokenAsync_Empty_Throws_ArgumentException() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.SetTokenAsync(""));
	}

	[Fact]
	public async Task GetPrStatusAsync_Empty_Keys_Returns_Empty() {
		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusAsync([]);
		Assert.Empty(result);
	}

	[Fact]
	public async Task GetPrStatusAsync_No_Identity_Returns_Disconnected_Entries() {
		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusAsync([
			new PrKey("octocat", "Hello-World", "main")
		]);
		PrStatus entry = Assert.Single(result.Values);
		Assert.False(entry.Connected);
		Assert.Null(entry.Pr);
	}

	[Fact]
	public async Task GetPrStatusByDirectoryAsync_No_Identity_No_Remote_Returns_Disconnected() {
		this.git.Remote = null;
		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusByDirectoryAsync([
			new DirectoryPrKey("/repo", "main")
		]);

		PrStatus entry = Assert.Single(result.Values);
		Assert.False(entry.Connected);
		Assert.Null(entry.Repo);
	}

	[Fact]
	public async Task GetPrStatusByDirectoryAsync_With_Resolved_Remote_But_No_Token_Returns_Disconnected_With_Repo_Hint() {
		this.git.Remote = new RemoteRef("origin", "octocat", "Hello-World", "https://github.com/octocat/Hello-World.git");

		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusByDirectoryAsync([
			new DirectoryPrKey("/repo", "feat/x")
		]);

		PrStatus entry = Assert.Single(result.Values);
		Assert.False(entry.Connected);
		Assert.Equal("octocat",     entry.Repo!.Owner);
		Assert.Equal("Hello-World", entry.Repo.Repo);
		Assert.Equal("feat/x",      entry.Branch);
		Assert.Equal("origin",      entry.ResolvedRemoteName);
	}

	[Fact]
	public async Task GetPrStatusByDirectoryAsync_Bubbles_Git_Errors_As_Disconnected_Per_Target() {
		this.git.ThrowOnGetRemote = new InvalidOperationException("locked");

		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusByDirectoryAsync([
			new DirectoryPrKey("/repo", "main")
		]);

		PrStatus entry = Assert.Single(result.Values);
		Assert.False(entry.Connected);
		Assert.Contains("locked", entry.Error);
	}

	[Fact]
	public void Token_RoundTrips_Through_DataProtection() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		string protectedToken = protector.Protect("ghp_secret_abc");
		Assert.NotEqual("ghp_secret_abc", protectedToken);
		Assert.Equal("ghp_secret_abc",     protector.Unprotect(protectedToken));
	}

	private sealed class FakeGitService :IGitService {
		public RemoteRef?  Remote            { get; set; }
		public Exception?  ThrowOnGetRemote  { get; set; }

		public Task<RemoteRef?> GetRemoteRefAsync(string path, string branch, CancellationToken ct = default) {
			if (this.ThrowOnGetRemote is not null) throw this.ThrowOnGetRemote;
			return Task.FromResult(this.Remote);
		}

		// Not exercised by these tests:
		public Task<RepoStatus>    GetRepoStatusAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<BranchListing> ListBranchesAsync(string path, CancellationToken ct = default)  => throw new NotImplementedException();
		public Task<IReadOnlyList<WorktreeMetadata>> ListWorktreesAsync(string projectPath, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<WorktreeMetadata> CreateWorktreeAsync(string projectPath, string newPath, string branch, CancellationToken ct = default) => throw new NotImplementedException();
		public Task                   RemoveWorktreeAsync(string projectPath, string worktreePath, bool force = false, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<GlobalGitIdentity?> GetGlobalIdentityAsync(CancellationToken ct = default) => throw new NotImplementedException();

		public Task<BranchRef> CreateBranchAsync(string path, string name, string? startPoint, CancellationToken ct = default) => throw new NotImplementedException();
		public Task            DeleteBranchAsync(string path, string name, bool   force,       CancellationToken ct = default) => throw new NotImplementedException();
		public Task<BranchRef> RenameBranchAsync(string path, string oldName, string newName,  CancellationToken ct = default) => throw new NotImplementedException();
		public Task<IReadOnlyList<StashEntryInfo>> ListStashesAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<StashSaveResult> SaveStashAsync(string path, string? message, bool includeUntracked, CancellationToken ct = default) => throw new NotImplementedException();
		public Task ApplyStashAsync(string path, string stashRef, CancellationToken ct = default) => throw new NotImplementedException();
		public Task PopStashAsync  (string path, string stashRef, CancellationToken ct = default) => throw new NotImplementedException();
		public Task DropStashAsync (string path, string stashRef, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<IReadOnlyDictionary<string, int>> CountStashFilesAsync(string path, IReadOnlyList<string> refs, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<CommitFileDiff>           GetCommitFileDiffAsync(string path, string commitSha, string filePath, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<MergeConflictDetails?>    GetConflictDetailsAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<WorktreeBootstrapStatus>  GetWorktreeBootstrapStatusAsync(string worktreePath, CancellationToken ct = default) => throw new NotImplementedException();
		public Task                           SetWorktreeBootstrapStatusAsync(string worktreePath, WorktreeBootstrapStatus status, CancellationToken ct = default) => throw new NotImplementedException();
	}
}
