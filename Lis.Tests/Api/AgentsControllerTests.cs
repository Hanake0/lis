using Lis.Api.Controllers;
using Lis.Api.Schemas.Agents;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="AgentsController"/> — the read-only agent surface. Uses
/// EF Core InMemory; the controller hits the DbContext directly so no service stub
/// is needed.
/// </summary>
public class AgentsControllerTests :IDisposable {
	private readonly LisDbContext     db;
	private readonly AgentsController sut;

	public AgentsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"agents-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db  = new TestDbContext(options);
		this.sut = new AgentsController(this.db);
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
