using Lis.Api.Controllers;
using Lis.Api.Schemas.Projects;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="ProjectMetadataController"/> — the per-project KV store
/// powering openchamberConfig.ts (notes, todos, worktree setup, plan files).
/// </summary>
public class ProjectMetadataControllerTests :IDisposable {
	private readonly LisDbContext               db;
	private readonly FakeTimeProvider           time;
	private readonly ProjectMetadataController  sut;

	public ProjectMetadataControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"proj-meta-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new ProjectMetadataController(this.db, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task List_404_When_Project_Missing() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.List("nope", default));
	}

	[Fact]
	public async Task List_Empty_Returns_Empty_Array_For_Existing_Project_With_No_Metadata() {
		ProjectEntity project = await this.SeedProjectAsync("p1");

		ActionResult<ProjectMetadataEntryDto[]> result = await this.sut.List(project.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(Assert.IsType<ProjectMetadataEntryDto[]>(ok.Value));
	}

	[Fact]
	public async Task Set_Inserts_A_New_Row_And_Returns_The_Saved_Entry() {
		ProjectEntity project = await this.SeedProjectAsync("p1");

		ActionResult<ProjectMetadataEntryDto> result = await this.sut.Set(
			project.ExternalId, "notes", new SetProjectMetadataRequest("\"hello world\""), default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectMetadataEntryDto dto = Assert.IsType<ProjectMetadataEntryDto>(ok.Value);
		Assert.Equal("notes",            dto.Key);
		Assert.Equal("\"hello world\"",  dto.Value);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);

		ProjectMetadataEntity persisted = Assert.Single(this.db.ProjectMetadata);
		Assert.Equal(project.Id,        persisted.ProjectId);
		Assert.Equal("\"hello world\"", persisted.Value);
	}

	[Fact]
	public async Task Set_Updates_An_Existing_Row_In_Place() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		await this.sut.Set(project.ExternalId, "notes", new SetProjectMetadataRequest("\"v1\""), default);
		this.time.Advance(TimeSpan.FromMinutes(5));

		ActionResult<ProjectMetadataEntryDto> result = await this.sut.Set(
			project.ExternalId, "notes", new SetProjectMetadataRequest("\"v2\""), default
		);

		ProjectMetadataEntryDto dto = Assert.IsType<ProjectMetadataEntryDto>(
			Assert.IsType<OkObjectResult>(result.Result).Value
		);
		Assert.Equal("\"v2\"", dto.Value);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);

		// Still one row, not two — `(project_id, key)` is unique.
		Assert.Single(this.db.ProjectMetadata);
	}

	[Fact]
	public async Task Set_Rejects_Whitespace_Key_With_ArgumentException() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.Set(project.ExternalId, "   ", new SetProjectMetadataRequest("\"x\""), default)
		);
	}

	[Fact]
	public async Task Set_Rejects_Invalid_Json_With_ArgumentException() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		// Bare unquoted text is not valid JSON; jsonb column would reject it on the way down,
		// so the controller surfaces the early 400 instead.
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.Set(project.ExternalId, "notes", new SetProjectMetadataRequest("not json"), default)
		);
	}

	[Fact]
	public async Task Set_Accepts_Json_Arrays_And_Objects() {
		ProjectEntity project = await this.SeedProjectAsync("p1");

		await this.sut.Set(project.ExternalId, "todos", new SetProjectMetadataRequest("[{\"id\":\"t1\"}]"), default);
		await this.sut.Set(project.ExternalId, "shape", new SetProjectMetadataRequest("{\"k\":1}"),         default);

		ProjectMetadataEntity todos = Assert.Single(this.db.ProjectMetadata, m => m.Key == "todos");
		ProjectMetadataEntity shape = Assert.Single(this.db.ProjectMetadata, m => m.Key == "shape");
		Assert.Equal("[{\"id\":\"t1\"}]", todos.Value);
		Assert.Equal("{\"k\":1}",         shape.Value);
	}

	[Fact]
	public async Task Get_Returns_Found_False_When_Row_Does_Not_Exist() {
		ProjectEntity project = await this.SeedProjectAsync("p1");

		ActionResult<ProjectMetadataValueDto> result = await this.sut.Get(project.ExternalId, "absent", default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectMetadataValueDto dto = Assert.IsType<ProjectMetadataValueDto>(ok.Value);
		Assert.False(dto.Found);
		Assert.Null (dto.Value);
		Assert.Null (dto.UpdatedAt);
	}

	[Fact]
	public async Task Get_Returns_Found_True_With_Value_When_Row_Exists() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		await this.sut.Set(project.ExternalId, "todos", new SetProjectMetadataRequest("[]"), default);

		ActionResult<ProjectMetadataValueDto> result = await this.sut.Get(project.ExternalId, "todos", default);

		ProjectMetadataValueDto dto = Assert.IsType<ProjectMetadataValueDto>(
			Assert.IsType<OkObjectResult>(result.Result).Value
		);
		Assert.True(dto.Found);
		Assert.Equal("[]",                  dto.Value);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);
	}

	[Fact]
	public async Task List_Returns_All_Keys_Alphabetical() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		await this.sut.Set(project.ExternalId, "notes",         new SetProjectMetadataRequest("\"n\""), default);
		await this.sut.Set(project.ExternalId, "todos",         new SetProjectMetadataRequest("\"t\""), default);
		await this.sut.Set(project.ExternalId, "setupCommands", new SetProjectMetadataRequest("\"s\""), default);

		ActionResult<ProjectMetadataEntryDto[]> result = await this.sut.List(project.ExternalId, default);

		ProjectMetadataEntryDto[] dtos = Assert.IsType<ProjectMetadataEntryDto[]>(
			Assert.IsType<OkObjectResult>(result.Result).Value
		);
		Assert.Equal(["notes", "setupCommands", "todos"], dtos.Select(d => d.Key));
	}

	[Fact]
	public async Task Delete_Returns_NoContent_And_Removes_The_Row() {
		ProjectEntity project = await this.SeedProjectAsync("p1");
		await this.sut.Set(project.ExternalId, "notes", new SetProjectMetadataRequest("\"v\""), default);

		IActionResult result = await this.sut.Delete(project.ExternalId, "notes", default);

		Assert.IsType<NoContentResult>(result);
		Assert.Empty(this.db.ProjectMetadata);
	}

	[Fact]
	public async Task Delete_Is_Idempotent_When_Key_Was_Absent() {
		ProjectEntity project = await this.SeedProjectAsync("p1");

		IActionResult result = await this.sut.Delete(project.ExternalId, "absent", default);

		Assert.IsType<NoContentResult>(result);
	}

	[Fact]
	public async Task Two_Projects_Can_Share_The_Same_Key_Independently() {
		ProjectEntity a = await this.SeedProjectAsync("a");
		ProjectEntity b = await this.SeedProjectAsync("b");

		await this.sut.Set(a.ExternalId, "notes", new SetProjectMetadataRequest("\"a-notes\""), default);
		await this.sut.Set(b.ExternalId, "notes", new SetProjectMetadataRequest("\"b-notes\""), default);

		ProjectMetadataValueDto fromA = Assert.IsType<ProjectMetadataValueDto>(
			Assert.IsType<OkObjectResult>((await this.sut.Get(a.ExternalId, "notes", default)).Result).Value
		);
		ProjectMetadataValueDto fromB = Assert.IsType<ProjectMetadataValueDto>(
			Assert.IsType<OkObjectResult>((await this.sut.Get(b.ExternalId, "notes", default)).Result).Value
		);
		Assert.Equal("\"a-notes\"", fromA.Value);
		Assert.Equal("\"b-notes\"", fromB.Value);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<ProjectEntity> SeedProjectAsync(string externalId) {
		ProjectEntity project = new() {
			ExternalId = externalId,
			Path       = $"/{externalId}",
			AddedAt    = this.time.GetUtcNow(),
			CreatedAt  = this.time.GetUtcNow(),
			UpdatedAt  = this.time.GetUtcNow(),
		};
		this.db.Projects.Add(project);
		await this.db.SaveChangesAsync();
		return project;
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}
}
