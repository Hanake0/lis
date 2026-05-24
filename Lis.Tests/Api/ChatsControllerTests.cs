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
	private readonly LisDbContext       db;
	private readonly FakeTimeProvider   time;
	private readonly CapturingEventBus  bus;
	private readonly ChatsController    sut;

	public ChatsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"chats-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus  = new CapturingEventBus();
		this.sut  = new ChatsController(this.db, this.bus, this.time);
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
}
