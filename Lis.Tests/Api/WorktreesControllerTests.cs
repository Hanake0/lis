using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Git;
using Lis.Core.Events;
using Lis.Git.Services;

using LibGit2Sharp;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

public sealed class WorktreesControllerTests :IDisposable {
	private readonly string              repoDir;
	private readonly FakeTimeProvider    time;
	private readonly CapturingEventBus   bus;
	private readonly WorktreesController sut;

	public WorktreesControllerTests() {
		this.repoDir = Path.Combine(Path.GetTempPath(), $"lis-wt-ctrl-{Guid.NewGuid():N}");
		Directory.CreateDirectory(this.repoDir);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus  = new CapturingEventBus();
		this.sut  = new WorktreesController(new GitService(), this.bus, this.time);
	}

	public void Dispose() {
		try { if (Directory.Exists(this.repoDir)) Directory.Delete(this.repoDir, recursive: true); } catch { /* ignore */ }
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task List_NonRepo_Returns_Empty() {
		ActionResult<WorktreeMetadataDto[]> result = await this.sut.List(this.repoDir, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(Assert.IsType<WorktreeMetadataDto[]>(ok.Value));
	}

	[Fact]
	public async Task List_Repo_Returns_Main_Worktree() {
		SeedRepoWithCommit(this.repoDir);

		ActionResult<WorktreeMetadataDto[]> result = await this.sut.List(this.repoDir, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		WorktreeMetadataDto[] dtos = Assert.IsType<WorktreeMetadataDto[]>(ok.Value);
		Assert.Single(dtos);
		Assert.Equal("(main)", dtos[0].Name);
	}

	[Fact]
	public async Task Create_Publishes_GitStatusChanged_Event() {
		SeedRepoWithCommit(this.repoDir);
		using (Repository repo = new(this.repoDir)) repo.CreateBranch("feature");

		string wtPath = Path.Combine(Path.GetTempPath(), $"lis-wt-{Guid.NewGuid():N}");
		try {
			ActionResult<WorktreeMetadataDto> result = await this.sut.Create(
				new CreateWorktreeRequest(this.repoDir, wtPath, "feature"),
				default
			);

			Assert.IsType<CreatedResult>(result.Result);
			ServerEvent ev = Assert.Single(this.bus.Captured);
			Assert.Equal(GitEventNames.StatusChanged, ev.Name);
			GitStatusChangedPayload payload = Assert.IsType<GitStatusChangedPayload>(ev.Data);
			Assert.Equal(this.repoDir, payload.Path);
		} finally {
			try { Directory.Delete(wtPath, recursive: true); } catch { /* ignore */ }
		}
	}

	[Fact]
	public async Task Remove_Missing_Worktree_Throws_KeyNotFound() {
		SeedRepoWithCommit(this.repoDir);
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.Remove(new RemoveWorktreeRequest(this.repoDir, Path.Combine(this.repoDir, "ghost"), Force: false), default));
	}

	[Fact]
	public async Task Remove_NonRepo_Throws_ArgumentException() {
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.Remove(new RemoveWorktreeRequest(this.repoDir, Path.Combine(this.repoDir, "wt"), Force: false), default));
	}

	// ── Helpers ────────────────────────────────────────────────────

	private static void SeedRepoWithCommit(string dir) {
		Repository.Init(dir);
		File.WriteAllText(Path.Combine(dir, "README.md"), "hello");
		using Repository repo = new(dir);
		Commands.Stage(repo, "README.md");
		Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
		repo.Commit("init", sig, sig);
	}

	private sealed class CapturingEventBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}
}
