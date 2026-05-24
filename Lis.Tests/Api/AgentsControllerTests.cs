using Lis.Api.Controllers;
using Lis.Api.Schemas.Agents;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="AgentsController"/> — the read-only agent surface. Uses
/// EF Core InMemory; the controller hits the DbContext directly so no service stub
/// is needed.
/// </summary>
public class AgentsControllerTests :IDisposable {
	private readonly LisDbContext     db;
	private readonly FakeTimeProvider time;
	private readonly AgentsController sut;

	public AgentsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"agents-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new AgentsController(this.db, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task List_Empty_Returns_Ok_With_Empty_Array() {
		ActionResult<AgentDto[]> result = await this.sut.List(default);
		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(Assert.IsType<AgentDto[]>(ok.Value));
	}

	[Fact]
	public async Task List_Built_Ins_Come_First_Then_Custom_Alphabetical() {
		await this.SeedAsync("zebra",        isDefault: false);
		await this.SeedAsync("alpha",        isDefault: false);
		await this.SeedAsync("default-bot",  isDefault: true);
		await this.SeedAsync("aaa-default",  isDefault: true);

		ActionResult<AgentDto[]> result = await this.sut.List(default);
		AgentDto[] dtos = Assert.IsType<AgentDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(["aaa-default", "default-bot", "alpha", "zebra"], dtos.Select(d => d.Name));
		Assert.True (dtos[0].Native);
		Assert.True (dtos[1].Native);
		Assert.False(dtos[2].Native);
	}

	[Fact]
	public async Task List_Carries_Through_Lis_Specific_Fields() {
		await this.SeedAsync("opus",
			displayName:    "Opus 4.7",
			thinkingEffort: "high",
			toolProfile:    "deep",
			maxTokens:      8192,
			contextBudget:  16000
		);

		ActionResult<AgentDto[]> result = await this.sut.List(default);
		AgentDto dto = Assert.IsType<AgentDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value).Single();
		Assert.Equal("Opus 4.7", dto.DisplayName);
		Assert.Equal("high",     dto.ThinkingEffort);
		Assert.Equal("deep",     dto.ToolProfile);
		Assert.Equal(8192,       dto.MaxTokens);
		Assert.Equal(16000,      dto.ContextBudget);
	}

	[Fact]
	public async Task Get_Returns_Ok_With_The_Mapped_Dto() {
		await this.SeedAsync("opus");

		ActionResult<AgentDto> result = await this.sut.Get("opus", default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal("opus", ((AgentDto)ok.Value!).Name);
	}

	[Fact]
	public async Task Get_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Get("missing", default));
	}

	// ── Create ────────────────────────────────────────────────────

	[Fact]
	public async Task Create_Persists_The_Agent_And_Returns_201_With_Mapped_Dto() {
		await this.SeedAsync("default-bot", isDefault: true);

		ActionResult<AgentDto> result = await this.sut.Create(
			new CreateAgentRequest(
				Name:           "helper",
				DisplayName:    "Helper",
				Description:    "helps with stuff",
				Model:          null,           // → defaults to system default's model
				ThinkingEffort: "low",
				ToolProfile:    "read-only",
				MaxTokens:      8000,
				ContextBudget:  20000,
				Mode:           "subagent",
				Prompt:         "you are a helper",
				Temperature:    0.7,
				TopP:           0.95,
				PermissionJson: "{\"bash\":\"ask\"}",
				Scope:          "user",
				Disabled:       false
			),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		AgentDto dto = Assert.IsType<AgentDto>(created.Value);
		Assert.Equal("helper",                  dto.Name);
		Assert.Equal("anthropic/claude",        dto.Model);    // defaulted from system default
		Assert.Equal("helps with stuff",        dto.Description);
		Assert.Equal(0.7,                       dto.Temperature);
		Assert.Equal("{\"bash\":\"ask\"}",      dto.PermissionJson);
		Assert.Equal("user",                    dto.Scope);
		Assert.False(dto.IsDefault);
	}

	[Fact]
	public async Task Create_Rejects_Duplicate_Name_With_409() {
		await this.SeedAsync("dup");

		ActionResult<AgentDto> result = await this.sut.Create(
			new CreateAgentRequest("dup", null, null, "anthropic/claude", null, null, null, null, null, null, null, null, null, null, null),
			default
		);

		ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
		ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
		Assert.Contains("already exists", problem.Detail!, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Create_Rejects_Invalid_PermissionJson_With_ArgumentException() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.Create(
			new CreateAgentRequest("bad", null, null, "anthropic/claude", null, null, null, null, null, null, null, null, "not json", null, null),
			default
		));
	}

	[Fact]
	public async Task Create_Falls_Back_To_Hardcoded_Model_When_No_Default_Agent_Exists() {
		ActionResult<AgentDto> result = await this.sut.Create(
			new CreateAgentRequest("new", null, null, null, null, null, null, null, null, null, null, null, null, null, null),
			default
		);
		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
		Assert.Equal("anthropic/claude-sonnet-4", dto.Model);
	}

	// ── Update ────────────────────────────────────────────────────

	[Fact]
	public async Task Update_Patches_Only_The_Fields_That_Are_Non_Null() {
		await this.SeedAsync("opus", maxTokens: 4096, contextBudget: 12000);

		ActionResult<AgentDto> result = await this.sut.Update(
			"opus",
			new UpdateAgentRequest(
				DisplayName: "Opus Updated",
				Description: "new desc",
				// All other fields null → unchanged.
				Model: null, ThinkingEffort: null, ToolProfile: null, MaxTokens: null, ContextBudget: null,
				Mode: null, Prompt: null, Temperature: null, TopP: null, PermissionJson: null, Scope: null, Disabled: null
			),
			default
		);

		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("Opus Updated", dto.DisplayName);
		Assert.Equal("new desc",     dto.Description);
		Assert.Equal(4096,           dto.MaxTokens);     // unchanged
		Assert.Equal(12000,          dto.ContextBudget); // unchanged
	}

	[Fact]
	public async Task Update_Empty_String_Clears_Optional_Fields_To_Null() {
		await this.SeedAsync("opus", displayName: "old", thinkingEffort: "high");

		ActionResult<AgentDto> result = await this.sut.Update(
			"opus",
			new UpdateAgentRequest(
				DisplayName: "", Description: null, Model: null, ThinkingEffort: "", ToolProfile: null,
				MaxTokens: null, ContextBudget: null, Mode: null, Prompt: null, Temperature: null,
				TopP: null, PermissionJson: null, Scope: null, Disabled: null
			),
			default
		);

		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Null(dto.DisplayName);
		Assert.Null(dto.ThinkingEffort);
	}

	[Fact]
	public async Task Update_Rejects_Invalid_PermissionJson_With_ArgumentException() {
		await this.SeedAsync("opus");
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.Update(
			"opus",
			new UpdateAgentRequest(null, null, null, null, null, null, null, null, null, null, null, "not json", null, null),
			default
		));
	}

	[Fact]
	public async Task Update_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Update(
			"missing",
			new UpdateAgentRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null),
			default
		));
	}

	// ── Delete ────────────────────────────────────────────────────

	[Fact]
	public async Task Delete_Removes_A_Custom_Agent_And_Returns_NoContent() {
		await this.SeedAsync("custom");

		IActionResult result = await this.sut.Delete("custom", default);

		Assert.IsType<NoContentResult>(result);
		Assert.Empty(this.db.Agents);
	}

	[Fact]
	public async Task Delete_Refuses_Built_In_With_409_And_Keeps_The_Row() {
		await this.SeedAsync("default-bot", isDefault: true);

		IActionResult result = await this.sut.Delete("default-bot", default);

		ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
		ProblemDetails problem = Assert.IsType<ProblemDetails>(conflict.Value);
		Assert.Contains("system default", problem.Detail!, StringComparison.OrdinalIgnoreCase);
		Assert.Single(this.db.Agents);
	}

	[Fact]
	public async Task Delete_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Delete("missing", default));
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task SeedAsync(
		string name,
		bool   isDefault       = false,
		string? displayName    = null,
		string? thinkingEffort = null,
		string? toolProfile    = null,
		int    maxTokens       = 4096,
		int    contextBudget   = 12000
	) {
		this.db.Agents.Add(new AgentEntity {
			Name           = name,
			DisplayName    = displayName,
			Model          = "anthropic/claude",
			ThinkingEffort = thinkingEffort,
			ToolProfile    = toolProfile,
			IsDefault      = isDefault,
			MaxTokens      = maxTokens,
			ContextBudget  = contextBudget,
			CreatedAt      = DateTimeOffset.UtcNow,
			UpdatedAt      = DateTimeOffset.UtcNow,
		});
		await this.db.SaveChangesAsync();
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}
}
