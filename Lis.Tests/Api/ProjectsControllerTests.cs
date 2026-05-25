using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Projects;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Controller-level tests for <see cref="ProjectsController"/>. We invoke the controller
/// methods directly with a real <see cref="ProjectService"/> backed by EF Core InMemory,
/// and a <see cref="CapturingEventBus"/> that records every published event so we can
/// assert on the SSE payloads without spinning up a Kestrel host.
/// </summary>
public class ProjectsControllerTests :IDisposable {
	private readonly LisDbContext       db;
	private readonly FakeTimeProvider   time;
	private readonly CapturingEventBus  bus;
	private readonly ProjectsController sut;

	public ProjectsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"projects-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus  = new CapturingEventBus();
		ProjectService service = new(this.db, this.time);
		this.sut  = new ProjectsController(service, this.bus, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	// ── List + Get ─────────────────────────────────────────────────

	[Fact]
	public async Task List_Empty_Returns_Ok_With_Empty_Array() {
		ActionResult<ProjectDto[]> result = await this.sut.List(default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto[] dtos = Assert.IsType<ProjectDto[]>(ok.Value);
		Assert.Empty(dtos);
	}

	[Fact]
	public async Task List_Returns_Projects_In_Sort_Order() {
		ProjectEntity a = await this.SeedAsync("/a");
		ProjectEntity b = await this.SeedAsync("/b");
		ProjectEntity c = await this.SeedAsync("/c");

		// b → sort 0, a → 1, c → 2
		await this.sut.Reorder(new ReorderProjectsRequest([b.ExternalId, a.ExternalId, c.ExternalId]), default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>((await this.sut.List(default)).Result);
		ProjectDto[] dtos = Assert.IsType<ProjectDto[]>(ok.Value);
		Assert.Equal([b.ExternalId, a.ExternalId, c.ExternalId], dtos.Select(d => d.Id));
	}

	[Fact]
	public async Task Get_Existing_Returns_Ok_With_Dto() {
		ProjectEntity project = await this.SeedAsync("/repo");

		ActionResult<ProjectDto> result = await this.sut.Get(project.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(ok.Value);
		Assert.Equal(project.ExternalId, dto.Id);
		Assert.Equal(project.Path,       dto.Path);
	}

	[Fact]
	public async Task Get_Missing_Returns_NotFound() {
		ActionResult<ProjectDto> result = await this.sut.Get("does-not-exist", default);

		NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
		Assert.Contains("does-not-exist", (string)notFound.Value!);
	}

	// ── Create ─────────────────────────────────────────────────────

	[Fact]
	public async Task Create_Returns_CreatedAtAction_And_Emits_Created_Event() {
		ActionResult<ProjectDto> result = await this.sut.Create(
			new CreateProjectRequest(Path: "/x/y", Label: "Custom", Color: "blue"),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(created.Value);
		Assert.Equal("Custom",                dto.Label);
		Assert.Equal("blue",                  dto.Color);
		Assert.Equal(nameof(this.sut.Get),    created.ActionName);
		Assert.Equal(dto.Id,                  created.RouteValues!["externalId"]);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.Created, ev.Name);
		ProjectCreatedPayload payload = Assert.IsType<ProjectCreatedPayload>(ev.Data);
		Assert.Same(dto, payload.Project);
	}

	[Fact]
	public async Task Create_Without_Label_Uses_Derived_Label() {
		ActionResult<ProjectDto> result = await this.sut.Create(
			new CreateProjectRequest(Path: "/home/user/my-app", Label: null, Color: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(created.Value);
		Assert.Equal("My App", dto.Label);
		Assert.NotNull(dto.Color); // auto-assigned
	}

	// ── Update ─────────────────────────────────────────────────────

	[Fact]
	public async Task Update_Mutates_Fields_And_Emits_Updated_Event() {
		ProjectEntity project = await this.SeedAsync("/old");

		this.time.Advance(TimeSpan.FromMinutes(1));
		ActionResult<ProjectDto> result = await this.sut.Update(
			project.ExternalId,
			new UpdateProjectRequest(Label: "New", Color: "red", Icon: "🚀", IconBackground: "#fff", SidebarCollapsed: true),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(ok.Value);
		Assert.Equal("New",  dto.Label);
		Assert.Equal("red",  dto.Color);
		Assert.Equal("🚀",   dto.Icon);
		Assert.Equal("#fff", dto.IconBackground);
		Assert.True(dto.SidebarCollapsed);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.Updated, ev.Name);
		Assert.IsType<ProjectUpdatedPayload>(ev.Data);
	}

	// ── Delete ─────────────────────────────────────────────────────

	[Fact]
	public async Task Delete_Returns_NoContent_And_Emits_Deleted_Event() {
		ProjectEntity project = await this.SeedAsync("/gone");

		IActionResult result = await this.sut.Delete(project.ExternalId, default);

		Assert.IsType<NoContentResult>(result);
		Assert.Null(await this.db.Projects.FirstOrDefaultAsync(p => p.ExternalId == project.ExternalId));

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.Deleted, ev.Name);
		ProjectDeletedPayload payload = Assert.IsType<ProjectDeletedPayload>(ev.Data);
		Assert.Equal(project.ExternalId, payload.ProjectId);
	}

	// ── Reorder ────────────────────────────────────────────────────

	[Fact]
	public async Task Reorder_Updates_Sort_Index_And_Emits_Updated_Per_Project() {
		ProjectEntity a = await this.SeedAsync("/a");
		ProjectEntity b = await this.SeedAsync("/b");
		ProjectEntity c = await this.SeedAsync("/c");

		IActionResult result = await this.sut.Reorder(
			new ReorderProjectsRequest([c.ExternalId, a.ExternalId, b.ExternalId]),
			default
		);

		Assert.IsType<NoContentResult>(result);
		Assert.Equal(3, this.bus.Captured.Count);
		Assert.All(this.bus.Captured, ev => Assert.Equal(ProjectEventNames.Updated, ev.Name));

		// SortIndex should match the new order
		List<ProjectEntity> after = await this.db.Projects.OrderBy(p => p.SortIndex).ToListAsync();
		Assert.Equal([c.ExternalId, a.ExternalId, b.ExternalId], after.Select(p => p.ExternalId));
	}

	[Fact]
	public async Task Reorder_Empty_Returns_NoContent_Without_Events() {
		IActionResult result = await this.sut.Reorder(new ReorderProjectsRequest([]), default);

		Assert.IsType<NoContentResult>(result);
		Assert.Empty(this.bus.Captured);
	}

	// ── Touch ──────────────────────────────────────────────────────

	[Fact]
	public async Task Touch_Updates_LastOpened_And_Does_Not_Emit_Event() {
		ProjectEntity project = await this.SeedAsync("/touched");
		Assert.Null(project.LastOpenedAt);

		this.time.Advance(TimeSpan.FromMinutes(5));
		ActionResult<ProjectDto> result = await this.sut.Touch(project.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(ok.Value);
		Assert.Equal(this.time.GetUtcNow(), dto.LastOpenedAt);

		// Touch is intentionally silent — see controller comment.
		Assert.Empty(this.bus.Captured);
	}

	// ── Icon ───────────────────────────────────────────────────────

	[Fact]
	public async Task SetIcon_Decodes_Base64_Persists_Blob_And_Emits_Updated_Event() {
		ProjectEntity project = await this.SeedAsync("/iconed");
		byte[] raw = [0x89, 0x50, 0x4E, 0x47];
		string base64 = Convert.ToBase64String(raw);

		ActionResult<ProjectDto> result = await this.sut.SetIcon(
			project.ExternalId,
			new SetIconRequest(Mime: "image/png", BlobBase64: base64, Source: "custom"),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(ok.Value);
		Assert.Equal("image/png", dto.IconImageMime);
		Assert.Equal("custom",    dto.IconImageSource);

		ProjectEntity reloaded = await this.db.Projects.FirstAsync(p => p.ExternalId == project.ExternalId);
		Assert.Equal(raw, reloaded.IconImageBlob);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.Updated, ev.Name);
	}

	[Fact]
	public async Task SetIcon_Invalid_Base64_Throws_ArgumentException() {
		ProjectEntity project = await this.SeedAsync("/badb64");

		ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => this.sut.SetIcon(
			project.ExternalId,
			new SetIconRequest(Mime: "image/png", BlobBase64: "not-base64!!", Source: "custom"),
			default
		));
		Assert.Contains("base64", ex.Message);
	}

	[Fact]
	public async Task RemoveIcon_Clears_Blob_And_Emits_Updated_Event() {
		ProjectEntity project = await this.SeedAsync("/withicon");
		await this.sut.SetIcon(
			project.ExternalId,
			new SetIconRequest(Mime: "image/png", BlobBase64: Convert.ToBase64String([0x01]), Source: "auto"),
			default
		);
		this.bus.Captured.Clear();

		ActionResult<ProjectDto> result = await this.sut.RemoveIcon(project.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ProjectDto dto = Assert.IsType<ProjectDto>(ok.Value);
		Assert.Null(dto.IconImageMime);
		Assert.Null(dto.IconImageSource);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.Updated, ev.Name);
	}

	// ── MoveChat ───────────────────────────────────────────────────

	[Fact]
	public async Task MoveChat_To_Project_Returns_NoContent_And_Emits_ChatMoved() {
		ProjectEntity project = await this.SeedAsync("/host");
		ChatEntity chat = await this.SeedChatAsync("chat-1");

		IActionResult result = await this.sut.MoveChat(
			chat.ExternalId,
			new MoveChatRequest(ProjectExternalId: project.ExternalId),
			default
		);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ProjectEventNames.ChatMoved, ev.Name);
		ChatMovedPayload payload = Assert.IsType<ChatMovedPayload>(ev.Data);
		Assert.Equal(chat.ExternalId,    payload.ChatId);
		Assert.Equal(project.ExternalId, payload.ProjectId);
	}

	[Fact]
	public async Task MoveChat_Detach_Emits_ChatMoved_With_Null_Project() {
		ProjectEntity project = await this.SeedAsync("/host");
		ChatEntity chat = await this.SeedChatAsync("chat-2");
		await this.sut.MoveChat(chat.ExternalId, new MoveChatRequest(project.ExternalId), default);
		this.bus.Captured.Clear();

		IActionResult result = await this.sut.MoveChat(
			chat.ExternalId,
			new MoveChatRequest(ProjectExternalId: null),
			default
		);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		ChatMovedPayload payload = Assert.IsType<ChatMovedPayload>(ev.Data);
		Assert.Equal(chat.ExternalId, payload.ChatId);
		Assert.Null(payload.ProjectId);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<ProjectEntity> SeedAsync(string path) {
		ProjectService service = new(this.db, this.time);
		return await service.CreateAsync(path);
	}

	private async Task<ChatEntity> SeedChatAsync(string externalId) {
		ChatEntity chat = new() {
			ExternalId = externalId,
			Channel    = "test",
			CreatedAt  = this.time.GetUtcNow(),
			UpdatedAt  = this.time.GetUtcNow(),
		};
		this.db.Chats.Add(chat);
		await this.db.SaveChangesAsync();
		return chat;
	}

	/// <summary>Ignores pgvector-specific model config that InMemory doesn't support.</summary>
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

		public IEventSubscription Subscribe(int capacity = 256, Func<ServerEvent, bool>? filter = null) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}
}
