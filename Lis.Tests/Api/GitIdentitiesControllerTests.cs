using Lis.Api.Controllers;
using Lis.Api.Schemas.Git;
using Lis.Git.Services;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="GitIdentitiesController"/>. Uses stub
/// <see cref="IGitIdentityService"/> and <see cref="IGitService"/> so we
/// don't talk to DB or LibGit2Sharp.
/// </summary>
public sealed class GitIdentitiesControllerTests {

	[Fact]
	public async Task List_Returns_All_Profiles() {
		StubIdentitySvc svc = new();
		svc.Items.Add(MakeEntity("a", "Work"));
		svc.Items.Add(MakeEntity("b", "Personal"));
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<GitIdentityDto[]> result = await sut.List(default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GitIdentityDto[] dtos = Assert.IsType<GitIdentityDto[]>(ok.Value);
		Assert.Equal(2, dtos.Length);
		Assert.Equal("Work",     dtos[0].Name);
		Assert.Equal("Personal", dtos[1].Name);
	}

	[Fact]
	public async Task Get_NotFound_When_Missing() {
		StubIdentitySvc svc = new();
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<GitIdentityDto> result = await sut.Get("nope", default);

		Assert.IsType<NotFoundObjectResult>(result.Result);
	}

	[Fact]
	public async Task Get_Returns_Ok_With_Mapped_Dto() {
		StubIdentitySvc svc = new();
		svc.Items.Add(MakeEntity("a", "Work"));
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<GitIdentityDto> result = await sut.Get("a", default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal("Work", ((GitIdentityDto)ok.Value!).Name);
	}

	[Fact]
	public async Task Create_Returns_201_With_The_Created_Dto() {
		StubIdentitySvc svc = new();
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<GitIdentityDto> result = await sut.Create(new CreateGitIdentityRequest(
			Id: null, Name: "Work", UserName: "u", UserEmail: "u@x.io",
			AuthType: "ssh", SshKeyPath: null, Host: null, Color: null, Icon: null
		), default);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		GitIdentityDto dto = Assert.IsType<GitIdentityDto>(created.Value);
		Assert.Equal("Work", dto.Name);
		Assert.NotEmpty(dto.Id);
		Assert.Single(svc.Items);
	}

	[Fact]
	public async Task Update_Applies_Only_Provided_Fields() {
		StubIdentitySvc svc = new();
		svc.Items.Add(MakeEntity("a", "Work", color: "red"));
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<GitIdentityDto> result = await sut.Update("a", new UpdateGitIdentityRequest(
			Name: "Renamed", UserName: null, UserEmail: null, AuthType: null,
			SshKeyPath: null, Host: null, Color: null, Icon: null
		), default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GitIdentityDto dto = Assert.IsType<GitIdentityDto>(ok.Value);
		Assert.Equal("Renamed", dto.Name);
		Assert.Equal("red",     dto.Color); // untouched
	}

	[Fact]
	public async Task Delete_Returns_NoContent_And_Invokes_Service() {
		StubIdentitySvc svc = new();
		svc.Items.Add(MakeEntity("a", "Work"));
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		IActionResult result = await sut.Delete("a", default);
		Assert.IsType<NoContentResult>(result);
		Assert.Empty(svc.Items);
	}

	[Fact]
	public async Task Global_Returns_NoContent_When_Host_Has_No_Global_Identity() {
		StubGitSvc git = new() { GlobalIdentity = null };
		GitIdentitiesController sut = new(new StubIdentitySvc(), git);

		ActionResult<GlobalGitIdentityDto> result = await sut.Global(default);
		Assert.IsType<NoContentResult>(result.Result);
	}

	[Fact]
	public async Task Global_Returns_Ok_With_Identity_When_Set() {
		StubGitSvc git = new() { GlobalIdentity = new GlobalGitIdentity("Octo Cat", "octo@gh.com", "ssh -i ~/.ssh/id_ed25519") };
		GitIdentitiesController sut = new(new StubIdentitySvc(), git);

		ActionResult<GlobalGitIdentityDto> result = await sut.Global(default);
		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GlobalGitIdentityDto dto = Assert.IsType<GlobalGitIdentityDto>(ok.Value);
		Assert.Equal("Octo Cat",                   dto.UserName);
		Assert.Equal("octo@gh.com",                dto.UserEmail);
		Assert.Equal("ssh -i ~/.ssh/id_ed25519",   dto.SshCommand);
	}

	[Fact]
	public async Task GetDefault_Returns_The_Setting() {
		StubIdentitySvc svc = new() { DefaultId = "a" };
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		ActionResult<DefaultGitIdentityDto> result = await sut.GetDefault(default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal("a", ((DefaultGitIdentityDto)ok.Value!).ExternalId);
	}

	[Fact]
	public async Task SetDefault_Returns_NoContent_And_Forwards() {
		StubIdentitySvc svc = new();
		GitIdentitiesController sut = new(svc, new StubGitSvc());

		IActionResult result = await sut.SetDefault(new SetDefaultGitIdentityRequest("b"), default);

		Assert.IsType<NoContentResult>(result);
		Assert.Equal("b", svc.DefaultId);
	}

	// ── Stubs ───────────────────────────────────────────────────────

	private static GitIdentityEntity MakeEntity(string id, string name, string? color = null) => new() {
		ExternalId = id,
		Name       = name,
		UserName   = "user",
		UserEmail  = "user@x.io",
		Color      = color,
		CreatedAt  = DateTimeOffset.UtcNow,
		UpdatedAt  = DateTimeOffset.UtcNow,
	};

	private sealed class StubIdentitySvc :IGitIdentityService {
		public List<GitIdentityEntity> Items     { get; } = [];
		public string?                 DefaultId { get; set; }

		public Task<IReadOnlyList<GitIdentityEntity>> ListAsync(CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<GitIdentityEntity>>(this.Items.ToList());

		public Task<GitIdentityEntity?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
			Task.FromResult(this.Items.FirstOrDefault(e => e.ExternalId == externalId));

		public Task<GitIdentityEntity> CreateAsync(GitIdentityEntity input, CancellationToken ct = default) {
			GitIdentityEntity row = new() {
				ExternalId = string.IsNullOrEmpty(input.ExternalId) ? Guid.NewGuid().ToString("N")[..16] : input.ExternalId,
				Name       = input.Name,
				UserName   = input.UserName,
				UserEmail  = input.UserEmail,
				AuthType   = input.AuthType,
				SshKeyPath = input.SshKeyPath,
				Host       = input.Host,
				Color      = input.Color,
				Icon       = input.Icon,
				CreatedAt  = DateTimeOffset.UtcNow,
				UpdatedAt  = DateTimeOffset.UtcNow,
			};
			this.Items.Add(row);
			return Task.FromResult(row);
		}

		public Task<GitIdentityEntity> UpdateAsync(string externalId, Action<GitIdentityEntity> mutate, CancellationToken ct = default) {
			GitIdentityEntity row = this.Items.First(e => e.ExternalId == externalId);
			mutate(row);
			row.UpdatedAt = DateTimeOffset.UtcNow;
			return Task.FromResult(row);
		}

		public Task DeleteAsync(string externalId, CancellationToken ct = default) {
			this.Items.RemoveAll(e => e.ExternalId == externalId);
			if (this.DefaultId == externalId) this.DefaultId = null;
			return Task.CompletedTask;
		}

		public Task<string?> GetDefaultIdentityExternalIdAsync(CancellationToken ct = default) =>
			Task.FromResult(this.DefaultId);

		public Task SetDefaultIdentityExternalIdAsync(string? externalId, CancellationToken ct = default) {
			this.DefaultId = externalId;
			return Task.CompletedTask;
		}
	}

	private sealed class StubGitSvc :IGitService {
		public GlobalGitIdentity? GlobalIdentity { get; set; }

		public Task<GlobalGitIdentity?> GetGlobalIdentityAsync(CancellationToken ct = default) =>
			Task.FromResult(this.GlobalIdentity);

		public Task<RepoStatus>    GetRepoStatusAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<BranchListing> ListBranchesAsync(string path, CancellationToken ct = default)  => throw new NotImplementedException();
		public Task<IReadOnlyList<WorktreeMetadata>> ListWorktreesAsync(string projectPath, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<WorktreeMetadata> CreateWorktreeAsync(string projectPath, string newPath, string branch, CancellationToken ct = default) => throw new NotImplementedException();
		public Task                   RemoveWorktreeAsync(string projectPath, string worktreePath, bool force = false, CancellationToken ct = default) => throw new NotImplementedException();
		public Task<RemoteRef?>       GetRemoteRefAsync(string path, string branch, CancellationToken ct = default) => throw new NotImplementedException();
	}
}
