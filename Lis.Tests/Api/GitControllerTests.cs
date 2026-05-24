using Lis.Api.Controllers;
using Lis.Api.Schemas.Git;
using Lis.Git.Services;

using LibGit2Sharp;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

public sealed class GitControllerTests :IDisposable {
	private readonly string         repoDir;
	private readonly GitController  sut;

	public GitControllerTests() {
		this.repoDir = Path.Combine(Path.GetTempPath(), $"lis-git-ctrl-{Guid.NewGuid():N}");
		Directory.CreateDirectory(this.repoDir);
		this.sut = new GitController(new GitService());
	}

	public void Dispose() {
		try { if (Directory.Exists(this.repoDir)) Directory.Delete(this.repoDir, recursive: true); } catch { /* ignore */ }
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task Status_NotARepo_Returns_Ok_With_Defaults() {
		ActionResult<RepoStatusDto> result = await this.sut.Status(this.repoDir, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		RepoStatusDto dto = Assert.IsType<RepoStatusDto>(ok.Value);
		Assert.False(dto.IsGitRepo);
		Assert.Null(dto.Branch);
		Assert.Null(dto.LastCommit);
	}

	[Fact]
	public async Task Status_With_Commit_Returns_Branch_And_LastCommit() {
		SeedRepoWithCommit(this.repoDir);

		ActionResult<RepoStatusDto> result = await this.sut.Status(this.repoDir, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		RepoStatusDto dto = Assert.IsType<RepoStatusDto>(ok.Value);
		Assert.True(dto.IsGitRepo);
		Assert.NotNull(dto.Branch);
		Assert.NotNull(dto.LastCommit);
	}

	[Fact]
	public async Task Branches_Returns_Local_And_Current() {
		SeedRepoWithCommit(this.repoDir);
		using (Repository repo = new(this.repoDir)) repo.CreateBranch("feature/x");

		ActionResult<BranchListingDto> result = await this.sut.Branches(this.repoDir, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		BranchListingDto dto = Assert.IsType<BranchListingDto>(ok.Value);
		Assert.Contains("feature/x", dto.Local);
		Assert.NotNull(dto.Current);
	}

	private static void SeedRepoWithCommit(string dir) {
		Repository.Init(dir);
		File.WriteAllText(Path.Combine(dir, "README.md"), "hello");
		using Repository repo = new(dir);
		Commands.Stage(repo, "README.md");
		Signature sig = new("Test", "test@example.com", DateTimeOffset.UtcNow);
		repo.Commit("init", sig, sig);
	}
}
