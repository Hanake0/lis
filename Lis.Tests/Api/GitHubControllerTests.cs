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
		StubGitHubService svc = new() { Status = new GitHubStatus(Connected: true, Login: "octocat", Scopes: "repo") };
		GitHubController sut = new(svc);

		ActionResult<GitHubStatusDto> result = await sut.Status(default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GitHubStatusDto dto = Assert.IsType<GitHubStatusDto>(ok.Value);
		Assert.True(dto.Connected);
		Assert.Equal("octocat", dto.Login);
	}

	[Fact]
	public async Task SetToken_Returns_Ok_With_Mapped_Dto() {
		StubGitHubService svc = new() { Status = new GitHubStatus(Connected: true, Login: "octocat", Scopes: null) };
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
	public async Task PrStatus_Maps_Keys_And_Wraps_Result() {
		StubGitHubService svc = new();
		svc.PrStatusResult["octocat/Hello-World@main"] = new PrStatus("open", null, null);
		GitHubController sut = new(svc);

		ActionResult<PrStatusResponse> result = await sut.PrStatus(
			new PrStatusRequest([new PrKeyDto("octocat", "Hello-World", "main")]),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		PrStatusResponse response = Assert.IsType<PrStatusResponse>(ok.Value);
		Assert.Single(response.Results);
		Assert.Equal("open", response.Results["octocat/Hello-World@main"].State);
	}

	private sealed class StubGitHubService :IGitHubService {
		public GitHubStatus Status                           { get; set; } = new(Connected: false, Login: null, Scopes: null);
		public string?      LastSetToken                     { get; private set; }
		public bool         CleardCalled                     { get; private set; }
		public Dictionary<string, PrStatus> PrStatusResult   { get; } = [];

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
	}
}
