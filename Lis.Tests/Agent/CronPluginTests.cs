using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Tools;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace Lis.Tests.Agent;

public class CronPluginTests : IDisposable {
	private readonly LisDbContext _db;
	private readonly CronPlugin _sut;

	public CronPluginTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this._db = new TestDbContext(options);

		Mock<IServiceScopeFactory> scopeFactory = new();
		Mock<IServiceScope> scope = new();
		Mock<IServiceProvider> provider = new();
		scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
		scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
		provider.Setup(p => p.GetService(typeof(LisDbContext))).Returns(this._db);

		this._sut = new CronPlugin(scopeFactory.Object);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) : LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	public void Dispose() {
		this._db.Dispose();
		GC.SuppressFinalize(this);
	}

	private async Task<ChatEntity> SeedChatAsync() {
		ChatEntity chat = new() {
			ExternalId = "test-chat@jid",
			Name = "Test Chat",
			Enabled = true,
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		this._db.Chats.Add(chat);
		await this._db.SaveChangesAsync();
		return chat;
	}

	// ── Add ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task CronAdd_ValidInput_CreatesJob() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		string result = await this._sut.CronAddAsync(
			"*/5 * * * *", "test_handler", "My Job");

		Assert.Contains("created", result, StringComparison.OrdinalIgnoreCase);

		CronJobEntity? job = await this._db.CronJobs.FirstOrDefaultAsync();
		Assert.NotNull(job);
		Assert.Equal("My Job", job.Name);
		Assert.Equal("*/5 * * * *", job.CronExpression);
		Assert.Equal("test_handler", job.Handler);
		Assert.True(job.IsDeterministic);
	}

	[Fact]
	public async Task CronAdd_NonDeterministic_SetsFlag() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		string result = await this._sut.CronAddAsync(
			"0 9 * * *", "ai_handler", "AI Job", isDeterministic: false);

		CronJobEntity? job = await this._db.CronJobs.FirstOrDefaultAsync();
		Assert.NotNull(job);
		Assert.False(job.IsDeterministic);
	}

	[Fact]
	public async Task CronAdd_InvalidCron_ReturnsError() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		string result = await this._sut.CronAddAsync(
			"invalid", "handler", "Bad Job");

		Assert.Contains("invalid", result, StringComparison.OrdinalIgnoreCase);
	}

	// ── List ────────────────────────────────────────────────────────────

	[Fact]
	public async Task CronList_NoJobs_ReturnsEmpty() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		string result = await this._sut.CronListAsync();

		Assert.Contains("no cron jobs", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task CronList_WithJobs_ListsThem() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		this._db.CronJobs.Add(new CronJobEntity {
			Name = "Daily Backup",
			CronExpression = "0 2 * * *",
			Handler = "backup_handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		string result = await this._sut.CronListAsync();

		Assert.Contains("Daily Backup", result);
		Assert.Contains("0 2 * * *", result);
		Assert.Contains("backup_handler", result);
	}

	// ── Remove ──────────────────────────────────────────────────────────

	[Fact]
	public async Task CronRemove_ExistingJob_RemovesIt() {
		ChatEntity chat = await this.SeedChatAsync();
		ToolContext.ChatId = chat.ExternalId;

		CronJobEntity job = new() {
			Name = "To Remove",
			CronExpression = "0 2 * * *",
			Handler = "handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		this._db.CronJobs.Add(job);
		await this._db.SaveChangesAsync();

		string result = await this._sut.CronRemoveAsync(job.Id);

		Assert.Contains("removed", result, StringComparison.OrdinalIgnoreCase);
		Assert.Null(await this._db.CronJobs.FindAsync(job.Id));
	}

	[Fact]
	public async Task CronRemove_NonExistent_ReturnsNotFound() {
		string result = await this._sut.CronRemoveAsync(9999);

		Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
	}
}
