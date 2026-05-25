using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Folders;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

public class SessionFoldersControllerTests :IDisposable {
	private readonly LisDbContext              db;
	private readonly FakeTimeProvider          time;
	private readonly CapturingEventBus         bus;
	private readonly SessionFolderService      service;
	private readonly SessionFoldersController  sut;

	public SessionFoldersControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"folders-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db      = new TestDbContext(options);
		this.time    = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus     = new CapturingEventBus();
		this.service = new SessionFolderService(this.db, this.time);
		this.sut     = new SessionFoldersController(this.service, this.bus, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	// ── List ───────────────────────────────────────────────────────

	[Fact]
	public async Task List_Filters_By_Scope() {
		await this.service.CreateAsync("dir:/x", "a");
		await this.service.CreateAsync("dir:/x", "b");
		await this.service.CreateAsync("dir:/y", "z");

		ActionResult<SessionFolderDto[]> result = await this.sut.List("dir:/x", default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		SessionFolderDto[] dtos = Assert.IsType<SessionFolderDto[]>(ok.Value);
		Assert.Equal(2, dtos.Length);
		Assert.All(dtos, d => Assert.Equal("dir:/x", d.ScopeKey));
	}

	// ── Get ────────────────────────────────────────────────────────

	[Fact]
	public async Task Get_Existing_Returns_Ok() {
		SessionFolderEntity folder = await this.service.CreateAsync("dir:/x", "F");

		ActionResult<SessionFolderDto> result = await this.sut.Get(folder.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal(folder.ExternalId, ((SessionFolderDto)ok.Value!).Id);
	}

	[Fact]
	public async Task Get_Missing_Returns_NotFound() {
		ActionResult<SessionFolderDto> result = await this.sut.Get("missing", default);
		Assert.IsType<NotFoundObjectResult>(result.Result);
	}

	// ── Create ─────────────────────────────────────────────────────

	[Fact]
	public async Task Create_Returns_CreatedAtAction_And_Emits_Created_Event() {
		ActionResult<SessionFolderDto> result = await this.sut.Create(
			new CreateSessionFolderRequest(ScopeKey: "dir:/x", Name: "Work", ParentId: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		SessionFolderDto dto = Assert.IsType<SessionFolderDto>(created.Value);
		Assert.Equal("Work",  dto.Name);
		Assert.Equal("dir:/x", dto.ScopeKey);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(FolderEventNames.Created, ev.Name);
		Assert.IsType<FolderCreatedPayload>(ev.Data);
	}

	// ── Rename ─────────────────────────────────────────────────────

	[Fact]
	public async Task Rename_Updates_Name_And_Emits_Updated_Event() {
		SessionFolderEntity folder = await this.service.CreateAsync("dir:/x", "Old");

		this.time.Advance(TimeSpan.FromMinutes(1));
		ActionResult<SessionFolderDto> result = await this.sut.Rename(
			folder.ExternalId,
			new RenameSessionFolderRequest("New"),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Equal("New", ((SessionFolderDto)ok.Value!).Name);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(FolderEventNames.Updated, ev.Name);
	}

	// ── Delete ─────────────────────────────────────────────────────

	[Fact]
	public async Task Delete_Returns_NoContent_And_Emits_Deleted_Event() {
		SessionFolderEntity folder = await this.service.CreateAsync("dir:/x", "Gone");

		IActionResult result = await this.sut.Delete(folder.ExternalId, default);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(FolderEventNames.Deleted, ev.Name);
		FolderDeletedPayload payload = Assert.IsType<FolderDeletedPayload>(ev.Data);
		Assert.Equal(folder.ExternalId, payload.FolderId);
	}

	// ── Reorder ────────────────────────────────────────────────────

	[Fact]
	public async Task Reorder_Updates_Sort_And_Emits_Updated_Per_Folder() {
		SessionFolderEntity a = await this.service.CreateAsync("dir:/x", "a");
		SessionFolderEntity b = await this.service.CreateAsync("dir:/x", "b");
		SessionFolderEntity c = await this.service.CreateAsync("dir:/x", "c");

		IActionResult result = await this.sut.Reorder(
			new ReorderSessionFoldersRequest("dir:/x", [c.ExternalId, b.ExternalId, a.ExternalId]),
			default
		);

		Assert.IsType<NoContentResult>(result);
		Assert.Equal(3, this.bus.Captured.Count);
		Assert.All(this.bus.Captured, ev => Assert.Equal(FolderEventNames.Updated, ev.Name));
	}

	// ── Add / Remove items ─────────────────────────────────────────

	[Fact]
	public async Task AddItems_Adds_Chats_And_Emits_Updated_Event() {
		SessionFolderEntity folder = await this.service.CreateAsync("dir:/x", "F");

		ActionResult<SessionFolderDto> result = await this.sut.AddItems(
			folder.ExternalId,
			new AddSessionsRequest(["a", "b"]),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		SessionFolderDto dto = Assert.IsType<SessionFolderDto>(ok.Value);
		Assert.Equal(["a", "b"], dto.Items.Select(i => i.ChatId));

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(FolderEventNames.Updated, ev.Name);
	}

	[Fact]
	public async Task RemoveItem_Removes_Chat_And_Emits_Updated_Event() {
		SessionFolderEntity folder = await this.service.CreateAsync("dir:/x", "F");
		await this.service.AddSessionsAsync(folder.ExternalId, ["a", "b"]);
		this.bus.Captured.Clear();

		ActionResult<SessionFolderDto> result = await this.sut.RemoveItem(folder.ExternalId, "a", default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		SessionFolderDto dto = Assert.IsType<SessionFolderDto>(ok.Value);
		Assert.Single(dto.Items);
		Assert.Equal("b", dto.Items[0].ChatId);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(FolderEventNames.Updated, ev.Name);
	}

	// ── Helpers ────────────────────────────────────────────────────

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
