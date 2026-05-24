using Lis.Api.Controllers;
using Lis.Api.Schemas.GitHub;
using Lis.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="GitHubController"/> using a stub <see cref="IGitHubService"/>
/// so we don't talk to GitHub or DataProtection.
/// </summary>
public sealed class GitHubControllerTests {

	[Fact]
	public async Task Status_Returns_Ok_With_Mapped_Dto() {
		StubGitHubService svc = new() { Status = new GitHubStatus(Connected: true, Login: "octocat", Scopes: "repo", AvatarUrl: "https://avatars/octocat.png") };
		GitHubController sut = new(svc);

		ActionResult<GitHubStatusDto> result = await sut.Status(default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GitHubStatusDto dto = Assert.IsType<GitHubStatusDto>(ok.Value);
		Assert.True(dto.Connected);
		Assert.Equal("octocat", dto.Login);
	}

	[Fact]
	public async Task SetToken_Returns_Ok_With_Mapped_Dto() {
		StubGitHubService svc = new() { Status = new GitHubStatus(Connected: true, Login: "octocat", Scopes: null, AvatarUrl: null) };
		GitHubController sut = new(svc);

		ActionResult<GitHubStatusDto> result = await sut.SetToken(new SetGitHubTokenRequest("ghp_xxx"), default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal("ghp_xxx", svc.LastSetToken);
		Assert.Equal("octocat", ((GitHubStatusDto)ok.Value!).Login);
	}

	[Fact]
	public async Task ClearToken_Returns_NoContent() {
		StubGitHubService svc = new();
		GitHubController sut = new(svc);

		IActionResult result = await sut.ClearToken(default);

		Assert.IsType<NoContentResult>(result);
		Assert.True(svc.CleardCalled);
	}

	[Fact]
	public async Task PrStatus_Maps_Keys_And_Wraps_Enriched_Result() {
		StubGitHubService svc = new();
		svc.PrStatusResult["octocat/Hello-World@main"] = new PrStatus(
			Connected:          true,
			Repo:               new PrRepoRef("octocat", "Hello-World"),
			Branch:             "main",
			Pr:                 new PullRequestDetail(
				Number: 7, Title: "ship it", Body: null, Url: "https://github.com/octocat/Hello-World/pull/7",
				State: "open", Draft: false, Base: "main", Head: "feat/x", HeadSha: "abc123",
				Mergeable: true, MergeableState: "clean"
			),
			Checks:             new ChecksSummary(State: "success", Total: 3, Pending: 0, Success: 3, Failure: 0),
			CanMerge:           true,
			DefaultBranch:      "main",
			ResolvedRemoteName: "origin",
			Error:              null
		);
		GitHubController sut = new(svc);

		ActionResult<PrStatusResponse> result = await sut.PrStatus(
			new PrStatusRequest([new PrKeyDto("octocat", "Hello-World", "main")]),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		PrStatusResponse response = Assert.IsType<PrStatusResponse>(ok.Value);
		Assert.Single(response.Results);
		PrStatusDto dto = response.Results["octocat/Hello-World@main"];
		Assert.True(dto.Connected);
		Assert.Equal("origin", dto.ResolvedRemoteName);
		Assert.Equal(7, dto.Pr!.Number);
		Assert.Equal("open", dto.Pr!.State);
		Assert.Equal(3, dto.Checks!.Success);
		Assert.Equal("success", dto.Checks.State);
		Assert.True(dto.CanMerge);
	}

	[Fact]
	public async Task PrStatusByDirectory_Wraps_Result_Keyed_By_DirectoryAndBranch() {
		StubGitHubService svc = new();
		svc.DirectoryPrResult["/repo::feat/x"] = new PrStatus(
			Connected:          true,
			Repo:               new PrRepoRef("octocat", "Hello-World"),
			Branch:             "feat/x",
			Pr:                 null,
			Checks:             null,
			CanMerge:           null,
			DefaultBranch:      "main",
			ResolvedRemoteName: "origin",
			Error:              null
		);
		GitHubController sut = new(svc);

		ActionResult<PrStatusResponse> result = await sut.PrStatusByDirectory(
			new PrStatusByDirectoryRequest([new DirectoryPrKeyDto("/repo", "feat/x")]),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		PrStatusResponse response = Assert.IsType<PrStatusResponse>(ok.Value);
		PrStatusDto dto = response.Results["/repo::feat/x"];
		Assert.True(dto.Connected);
		Assert.Null(dto.Pr);
		Assert.Equal("origin", dto.ResolvedRemoteName);
		Assert.Equal("octocat", dto.Repo!.Owner);
	}

	[Fact]
	public async Task PrStatusByDirectory_Returns_Disconnected_For_Unresolved_Targets() {
		StubGitHubService svc = new();
		svc.DirectoryPrResult["/no-repo::main"] = PrStatus.Disconnected();
		GitHubController sut = new(svc);

		ActionResult<PrStatusResponse> result = await sut.PrStatusByDirectory(
			new PrStatusByDirectoryRequest([new DirectoryPrKeyDto("/no-repo", "main")]),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		PrStatusResponse response = Assert.IsType<PrStatusResponse>(ok.Value);
		PrStatusDto dto = response.Results["/no-repo::main"];
		Assert.False(dto.Connected);
		Assert.Null(dto.Pr);
		Assert.Null(dto.Checks);
	}

	private sealed class StubGitHubService :IGitHubService {
		public GitHubStatus                   Status                  { get; set; } = new(Connected: false, Login: null, Scopes: null, AvatarUrl: null);
		public string?                        LastSetToken            { get; private set; }
		public bool                           CleardCalled            { get; private set; }
		public Dictionary<string, PrStatus>   PrStatusResult          { get; } = [];
		public Dictionary<string, PrStatus>   DirectoryPrResult       { get; } = [];

		public Task<GitHubStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(this.Status);

		public Task<GitHubStatus> SetTokenAsync(string token, CancellationToken ct = default) {
			this.LastSetToken = token;
			return Task.FromResult(this.Status);
		}

		public Task ClearTokenAsync(CancellationToken ct = default) {
			this.CleardCalled = true;
			return Task.CompletedTask;
		}

		public Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusAsync(IReadOnlyList<PrKey> keys, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyDictionary<string, PrStatus>>(this.PrStatusResult);

		public Task<IReadOnlyDictionary<string, PrStatus>> GetPrStatusByDirectoryAsync(IReadOnlyList<DirectoryPrKey> targets, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyDictionary<string, PrStatus>>(this.DirectoryPrResult);
	}
}
