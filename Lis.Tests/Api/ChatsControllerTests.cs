using Lis.Agent;
using Lis.Api.Controllers;
using Lis.Api.Schemas.Chats;
using Lis.Api.Schemas.Events;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="ChatsController"/>'s create endpoint — the one we need wired
/// for the multi-run orchestrator to mint chats from the webview. Backed by EF Core
/// InMemory + a capturing <see cref="IEventBus"/>.
/// </summary>
public class ChatsControllerTests :IDisposable {
	private readonly LisDbContext            db;
	private readonly FakeTimeProvider        time;
	private readonly CapturingEventBus       bus;
	private readonly FakeGenerationService   generation;
	private readonly ChatsController         sut;

	public ChatsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"chats-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db         = new TestDbContext(options);
		this.time       = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus        = new CapturingEventBus();
		this.generation = new FakeGenerationService();
		this.sut        = new ChatsController(this.db, this.bus, this.time, this.generation);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task Create_Without_ChatId_Mints_Web_Prefixed_Id_And_Returns_201() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "Hello", ProjectExternalId: null, Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.StartsWith("web:", dto.ChatId);
		Assert.Equal("Hello", dto.Name);
		Assert.Null(dto.ProjectExternalId);
		Assert.Equal(this.time.GetUtcNow(), dto.CreatedAt);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal(dto.ChatId, persisted.ExternalId);
		Assert.Equal("web",      persisted.Channel);
		Assert.True (persisted.Enabled);
	}

	[Fact]
	public async Task Create_With_Explicit_ChatId_Uses_It_Verbatim_And_Trims_Whitespace() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "  my-chat  ", Name: "  Trimmed  ", ProjectExternalId: null, Channel: "  ide  "),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("my-chat",  dto.ChatId);
		Assert.Equal("Trimmed",  dto.Name);
		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal("ide",      persisted.Channel);
	}

	[Fact]
	public async Task Create_Without_Name_Defaults_To_New_Chat() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "c-1", Name: null, ProjectExternalId: null, Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("New Chat", dto.Name);
	}

	[Fact]
	public async Task Create_Without_Channel_Defaults_To_Web() {
		await this.sut.Create(
			new CreateChatRequest(ChatId: "c-1", Name: "x", ProjectExternalId: null, Channel: null),
			default
		);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal("web", persisted.Channel);
	}

	[Fact]
	public async Task Create_With_ProjectId_Binds_The_Project() {
		ProjectEntity project = await this.SeedProjectAsync("proj-1", "/repo");

		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "x", ProjectExternalId: "proj-1", Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("proj-1", dto.ProjectExternalId);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal(project.Id, persisted.ProjectId);
	}

	[Fact]
	public async Task Create_With_Unknown_Project_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "x", ProjectExternalId: "missing-project", Channel: null),
			default
		));

		// Nothing persisted, nothing emitted — the SaveChanges never ran.
		Assert.Empty(this.db.Chats);
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task Create_Emits_Single_ChatCreated_Event_With_The_New_Dto() {
		await this.sut.Create(
			new CreateChatRequest(ChatId: "c-evt", Name: "x", ProjectExternalId: null, Channel: null),
			default
		);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Created, ev.Name);
		ChatCreatedPayload payload = Assert.IsType<ChatCreatedPayload>(ev.Data);
		ChatDto carried = Assert.IsType<ChatDto>(payload.Chat);
		Assert.Equal("c-evt", carried.ChatId);
	}

	[Fact]
	public async Task Create_Is_Idempotent_On_Existing_ChatId_Returning_200_And_Not_Re_Emitting() {
		// First POST: 201 + event.
		await this.sut.Create(
			new CreateChatRequest(ChatId: "dup", Name: "first", ProjectExternalId: null, Channel: null),
			default
		);
		Assert.Single(this.bus.Captured);

		// Second POST with the same id: 200, returns the *existing* row, no new event.
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "dup", Name: "second-tries-to-rename", ProjectExternalId: null, Channel: null),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(ok.Value);
		Assert.Equal("dup",   dto.ChatId);
		Assert.Equal("first", dto.Name); // existing name preserved — not "second-tries-to-rename"
		Assert.Single(this.bus.Captured); // still just the one event
		Assert.Single(this.db.Chats);     // no duplicate row
	}

	// ── List ──────────────────────────────────────────────────────

	[Fact]
	public async Task List_Empty_Returns_Ok_With_Empty_Array() {
		ActionResult<ChatDto[]> result = await this.sut.List(default);
		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(Assert.IsType<ChatDto[]>(ok.Value));
	}

	[Fact]
	public async Task List_Returns_All_Chats_Newest_Updated_First() {
		await this.SeedChatAsync("oldest", updatedHoursAgo: 5);
		await this.SeedChatAsync("middle", updatedHoursAgo: 2);
		await this.SeedChatAsync("newest", updatedHoursAgo: 0);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(["newest", "middle", "oldest"], dtos.Select(d => d.ChatId));
	}

	[Fact]
	public async Task List_Includes_Archived_Chats_With_Archived_At_Populated() {
		await this.SeedChatAsync("active",   updatedHoursAgo: 1);
		await this.SeedChatAsync("archived", updatedHoursAgo: 2, archivedHoursAgo: 1);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(2, dtos.Length);
		ChatDto archived = dtos.Single(d => d.ChatId == "archived");
		Assert.NotNull(archived.ArchivedAt);
		Assert.Null(dtos.Single(d => d.ChatId == "active").ArchivedAt);
	}

	[Fact]
	public async Task List_Includes_Project_External_Id_When_The_Chat_Is_Bound_To_One() {
		ProjectEntity project = await this.SeedProjectAsync("proj-x", "/repo");
		await this.SeedChatAsync("bound",   updatedHoursAgo: 0, projectId: project.Id);
		await this.SeedChatAsync("orphan",  updatedHoursAgo: 1);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal("proj-x", dtos.Single(d => d.ChatId == "bound").ProjectExternalId);
		Assert.Null(dtos.Single(d => d.ChatId == "orphan").ProjectExternalId);
	}

	// ── Rename (PATCH) ────────────────────────────────────────────

	[Fact]
	public async Task Update_Renames_The_Chat_And_Returns_The_Patched_Dto() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Update("rn", new UpdateChatRequest(Name: "  fresh name  "), default);

		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("fresh name", dto.Name);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Updated, ev.Name);
		Assert.IsType<ChatUpdatedPayload>(ev.Data);
	}

	[Fact]
	public async Task Update_Rejects_Empty_Name_With_ArgumentException() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.Update("rn", new UpdateChatRequest(Name: "   "), default));
	}

	[Fact]
	public async Task Update_With_Null_Name_Is_A_Noop_For_Name_But_Still_Bumps_UpdatedAt() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		ActionResult<ChatDto> result = await this.sut.Update("rn", new UpdateChatRequest(Name: null), default);
		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("rn", dto.Name);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);
	}

	[Fact]
	public async Task Update_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Update("missing", new UpdateChatRequest(Name: "x"), default));
	}

	// ── Archive ───────────────────────────────────────────────────

	[Fact]
	public async Task Archive_Sets_ArchivedAt_And_Emits_Event() {
		await this.SeedChatAsync("ar", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Archive("ar", default);
		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.NotNull(dto.ArchivedAt);
		Assert.Equal(this.time.GetUtcNow(), dto.ArchivedAt);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Archived, ev.Name);
		Assert.IsType<ChatArchivedPayload>(ev.Data);
	}

	[Fact]
	public async Task Archive_Already_Archived_Is_Idempotent_And_Does_Not_Re_Emit() {
		await this.SeedChatAsync("ar", updatedHoursAgo: 1, archivedHoursAgo: 0.5);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Archive("ar", default);
		Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task Archive_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Archive("missing", default));
	}

	// ── Delete ────────────────────────────────────────────────────

	[Fact]
	public async Task Delete_Removes_The_Chat_And_Returns_204() {
		await this.SeedChatAsync("del", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		IActionResult result = await this.sut.Delete("del", default);

		Assert.IsType<NoContentResult>(result);
		Assert.Empty(this.db.Chats);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Deleted, ev.Name);
		ChatDeletedPayload payload = Assert.IsType<ChatDeletedPayload>(ev.Data);
		Assert.Equal("del", payload.ChatId);
	}

	[Fact]
	public async Task Delete_Missing_Is_Idempotent_And_Still_Publishes_The_Deletion_Event() {
		this.bus.Captured.Clear();
		IActionResult result = await this.sut.Delete("never-existed", default);
		Assert.IsType<NoContentResult>(result);
		// Idempotent broadcast — racing clients still get the "drop this row" signal.
		Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Deleted, this.bus.Captured[0].Name);
	}

	// ── Generate ──────────────────────────────────────────────────

	[Fact]
	public async Task Generate_Calls_Generation_Service_With_Composed_Request_And_Returns_The_Text() {
		this.generation.ReturnedText  = "{\"subject\":\"feat: thing\",\"highlights\":[\"a\",\"b\"]}";
		this.generation.InputTokens   = 123;
		this.generation.OutputTokens  = 45;

		ActionResult<GenerateResponseDto> result = await this.sut.Generate(
			"chat-1",
			new GenerateRequest(
				VisiblePrompt: "Summarise the staged changes",
				HiddenPrompt:  "{ subject: string, highlights: string[] }",
				Kind:          "commit",
				Model:         "claude-sonnet-4"
			),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GenerateResponseDto dto = Assert.IsType<GenerateResponseDto>(ok.Value);
		Assert.Equal(this.generation.ReturnedText, dto.Text);
		Assert.Equal(123,                          dto.InputTokens);
		Assert.Equal(45,                           dto.OutputTokens);

		ChatGenerationService.GenerationRequest forwarded = Assert.Single(this.generation.Calls);
		Assert.Equal("chat-1",                                       forwarded.ChatExternalId);
		Assert.Equal("Summarise the staged changes",                 forwarded.VisiblePrompt);
		Assert.Equal("{ subject: string, highlights: string[] }",    forwarded.HiddenPrompt);
		Assert.Equal("commit",                                       forwarded.Kind);
		Assert.Equal("claude-sonnet-4",                              forwarded.ModelOverride);
	}

	[Fact]
	public async Task Generate_Defaults_Kind_To_Generate_When_Omitted() {
		await this.sut.Generate(
			"chat-1",
			new GenerateRequest(VisiblePrompt: "hi", HiddenPrompt: null, Kind: null, Model: null),
			default
		);

		ChatGenerationService.GenerationRequest forwarded = Assert.Single(this.generation.Calls);
		Assert.Equal("generate", forwarded.Kind);
	}

	[Fact]
	public async Task Create_Idempotent_Hit_Includes_Project_So_Dto_Carries_ProjectExternalId() {
		await this.SeedProjectAsync("p1", "/r");
		await this.sut.Create(
			new CreateChatRequest(ChatId: "with-proj", Name: "x", ProjectExternalId: "p1", Channel: null),
			default
		);

		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "with-proj", Name: "x", ProjectExternalId: "p1", Channel: null),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(ok.Value);
		Assert.Equal("p1", dto.ProjectExternalId); // require the Include(c => c.Project) round-trip
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task SeedChatAsync(
		string  externalId,
		double  updatedHoursAgo,
		double? archivedHoursAgo = null,
		long?   projectId        = null
	) {
		DateTimeOffset now = this.time.GetUtcNow();
		this.db.Chats.Add(new ChatEntity {
			ExternalId = externalId,
			Name       = externalId,
			Channel    = "web",
			ProjectId  = projectId,
			CreatedAt  = now.AddHours(-updatedHoursAgo - 1),
			UpdatedAt  = now.AddHours(-updatedHoursAgo),
			ArchivedAt = archivedHoursAgo is null ? null : now.AddHours(-archivedHoursAgo.Value),
			Enabled    = true,
		});
		await this.db.SaveChangesAsync();
	}

	private async Task<ProjectEntity> SeedProjectAsync(string externalId, string path) {
		ProjectEntity project = new() {
			ExternalId = externalId,
			Path       = path,
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

	/// <summary>
	/// Captures generation requests + returns a canned <c>{"text":"..."}</c> body so
	/// the Generate test can assert the prompt/model wiring without spinning up a real LLM.
	/// </summary>
	private sealed class FakeGenerationService :IChatGenerationService {
		public List<ChatGenerationService.GenerationRequest> Calls { get; } = [];
		public string ReturnedText  { get; set; } = "{\"subject\":\"chore: stub\",\"highlights\":[]}";
		public int    InputTokens   { get; set; }
		public int    OutputTokens  { get; set; }

		public Task<ChatGenerationService.GenerationResult> GenerateAsync(
			ChatGenerationService.GenerationRequest request,
			CancellationToken                        ct
		) {
			this.Calls.Add(request);
			return Task.FromResult(new ChatGenerationService.GenerationResult(this.ReturnedText, this.InputTokens, this.OutputTokens));
		}
	}
}
