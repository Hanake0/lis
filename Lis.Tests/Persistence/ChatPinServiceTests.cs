using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Persistence;

public class ChatPinServiceTests :IDisposable {
	private readonly LisDbContext     db;
	private readonly FakeTimeProvider time;
	private readonly ChatPinService   sut;

	public ChatPinServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"pins-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new ChatPinService(this.db, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
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
	public async Task PinAsync_Sets_Pinned_At_And_Updated_At() {
		ChatEntity chat = await this.SeedChatAsync("c-1");
		this.time.Advance(TimeSpan.FromMinutes(5));

		ChatEntity pinned = await this.sut.PinAsync(chat.ExternalId);

		Assert.NotNull(pinned.PinnedAt);
		Assert.Equal(this.time.GetUtcNow(), pinned.PinnedAt);
		Assert.Equal(this.time.GetUtcNow(), pinned.UpdatedAt);
	}

	[Fact]
	public async Task PinAsync_Refreshes_Timestamp_On_Repeated_Pin() {
		ChatEntity chat = await this.SeedChatAsync("c-1");
		DateTimeOffset firstPinAt = (await this.sut.PinAsync(chat.ExternalId)).PinnedAt!.Value;
		this.time.Advance(TimeSpan.FromMinutes(5));
		DateTimeOffset secondPinAt = (await this.sut.PinAsync(chat.ExternalId)).PinnedAt!.Value;

		Assert.NotEqual(firstPinAt, secondPinAt);
		Assert.Equal(this.time.GetUtcNow(), secondPinAt);
	}

	[Fact]
	public async Task PinAsync_Unknown_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.PinAsync("nope"));
	}

	[Fact]
	public async Task UnpinAsync_Clears_Pinned_At() {
		ChatEntity chat = await this.SeedChatAsync("c-1");
		await this.sut.PinAsync(chat.ExternalId);
		this.time.Advance(TimeSpan.FromMinutes(2));

		ChatEntity unpinned = await this.sut.UnpinAsync(chat.ExternalId);

		Assert.Null(unpinned.PinnedAt);
		Assert.Equal(this.time.GetUtcNow(), unpinned.UpdatedAt);
	}

	[Fact]
	public async Task UnpinAsync_Already_Unpinned_Is_NoOp_For_UpdatedAt() {
		ChatEntity chat = await this.SeedChatAsync("c-1");
		DateTimeOffset originalUpdated = chat.UpdatedAt;
		this.time.Advance(TimeSpan.FromMinutes(2));

		ChatEntity result = await this.sut.UnpinAsync(chat.ExternalId);

		Assert.Null(result.PinnedAt);
		Assert.Equal(originalUpdated, result.UpdatedAt); // unchanged because nothing to do
	}

	[Fact]
	public async Task UnpinAsync_Unknown_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.UnpinAsync("nope"));
	}
}
