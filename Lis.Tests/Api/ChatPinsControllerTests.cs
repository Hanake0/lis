using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

public class ChatPinsControllerTests :IDisposable {
	private readonly LisDbContext        db;
	private readonly FakeTimeProvider    time;
	private readonly CapturingEventBus   bus;
	private readonly ChatPinService      service;
	private readonly ChatPinsController  sut;

	public ChatPinsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"pins-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db      = new TestDbContext(options);
		this.time    = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus     = new CapturingEventBus();
		this.service = new ChatPinService(this.db, this.time);
		this.sut     = new ChatPinsController(this.service, this.bus, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
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

	[Fact]
	public async Task Pin_Returns_NoContent_And_Emits_Pinned_Event_With_Timestamp() {
		ChatEntity chat = await this.SeedChatAsync("c-1");

		IActionResult result = await this.sut.Pin(chat.ExternalId, default);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatPinEventNames.Pinned, ev.Name);
		ChatPinnedPayload payload = Assert.IsType<ChatPinnedPayload>(ev.Data);
		Assert.Equal(chat.ExternalId,       payload.ChatId);
		Assert.Equal(this.time.GetUtcNow(), payload.PinnedAt);
	}

	[Fact]
	public async Task Pin_Missing_Chat_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Pin("missing", default));
	}

	[Fact]
	public async Task Unpin_Returns_NoContent_And_Emits_Unpinned_Event() {
		ChatEntity chat = await this.SeedChatAsync("c-1");
		await this.sut.Pin(chat.ExternalId, default);
		this.bus.Captured.Clear();

		IActionResult result = await this.sut.Unpin(chat.ExternalId, default);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatPinEventNames.Unpinned, ev.Name);
		ChatUnpinnedPayload payload = Assert.IsType<ChatUnpinnedPayload>(ev.Data);
		Assert.Equal(chat.ExternalId, payload.ChatId);
	}

	[Fact]
	public async Task Unpin_Missing_Chat_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Unpin("missing", default));
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
