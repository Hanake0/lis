using Cronos;

using Lis.Agent;
using Lis.Core.Channel;
using Lis.Core.Cron;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Lis.Tests.Agent;

public class CronServiceTests : IDisposable {
	private readonly LisDbContext _db;
	private readonly Mock<IServiceScopeFactory> _scopeFactory;
	private readonly Mock<IServiceScope> _scope;
	private readonly Mock<IServiceProvider> _scopeProvider;
	private readonly Mock<IChannelClient> _channelClient;
	private readonly CronService _sut;

	public CronServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this._db = new TestDbContext(options);

		this._scopeFactory = new Mock<IServiceScopeFactory>();
		this._scope = new Mock<IServiceScope>();
		this._scopeProvider = new Mock<IServiceProvider>();
		this._channelClient = new Mock<IChannelClient>();

		this._scope.Setup(s => s.ServiceProvider).Returns(this._scopeProvider.Object);
		this._scopeFactory.Setup(f => f.CreateScope()).Returns(this._scope.Object);
		this._scopeProvider.Setup(p => p.GetService(typeof(LisDbContext))).Returns(this._db);
		this._scopeProvider.Setup(p => p.GetService(typeof(IChannelClient))).Returns(this._channelClient.Object);

		this._sut = new CronService(
			this._scopeFactory.Object,
			[],
			NullLogger<CronService>.Instance);
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

	// ── Cron Expression Parsing ─────────────────────────────────────────

	[Theory]
	[InlineData("*/5 * * * *")]     // Every 5 minutes
	[InlineData("0 9 * * *")]       // Daily at 9am
	[InlineData("0 0 * * 1")]       // Weekly on Monday
	[InlineData("30 14 1 * *")]     // Monthly on 1st at 14:30
	public void ParseCronExpression_ValidExpression_Succeeds(string expression) {
		CronExpression cron = CronExpression.Parse(expression);
		DateTimeOffset? next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

		Assert.NotNull(next);
		Assert.True(next > DateTimeOffset.UtcNow);
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("")]
	[InlineData("* * *")]
	public void ParseCronExpression_InvalidExpression_Throws(string expression) {
		Assert.ThrowsAny<Exception>(() => CronExpression.Parse(expression));
	}

	// ── Next Run Calculation ────────────────────────────────────────────

	[Fact]
	public void CalculateNextRun_EveryMinute_ReturnsWithinOneMinute() {
		CronExpression cron = CronExpression.Parse("* * * * *");
		DateTimeOffset now = DateTimeOffset.UtcNow;
		DateTimeOffset? next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

		Assert.NotNull(next);
		Assert.True((next.Value - now).TotalMinutes <= 1);
	}

	[Fact]
	public void CalculateNextRun_DailyAt9_ReturnsCorrectHour() {
		CronExpression cron = CronExpression.Parse("0 9 * * *");
		DateTimeOffset baseTime = new(2026, 3, 22, 0, 0, 0, TimeSpan.Zero);
		DateTimeOffset? next = cron.GetNextOccurrence(baseTime, TimeZoneInfo.Utc);

		Assert.NotNull(next);
		Assert.Equal(9, next.Value.Hour);
		Assert.Equal(0, next.Value.Minute);
	}

	// ── Due Job Detection ───────────────────────────────────────────────

	[Fact]
	public async Task GetDueJobs_ReturnsDueEnabledJobs() {
		ChatEntity chat = await this.SeedChatAsync();

		this._db.CronJobs.Add(new CronJobEntity {
			Name = "due-job",
			CronExpression = "* * * * *",
			Handler = "test_handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		this._db.CronJobs.Add(new CronJobEntity {
			Name = "future-job",
			CronExpression = "* * * * *",
			Handler = "test_handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		this._db.CronJobs.Add(new CronJobEntity {
			Name = "disabled-due-job",
			CronExpression = "* * * * *",
			Handler = "test_handler",
			ChatId = chat.Id,
			Enabled = false,
			NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		List<CronJobEntity> dueJobs = await this._db.CronJobs
			.Where(j => j.Enabled && j.NextRunAt <= DateTimeOffset.UtcNow)
			.ToListAsync();

		Assert.Single(dueJobs);
		Assert.Equal("due-job", dueJobs[0].Name);
	}

	[Fact]
	public async Task GetDueJobs_NoDueJobs_ReturnsEmpty() {
		ChatEntity chat = await this.SeedChatAsync();

		this._db.CronJobs.Add(new CronJobEntity {
			Name = "future-job",
			CronExpression = "* * * * *",
			Handler = "test_handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		List<CronJobEntity> dueJobs = await this._db.CronJobs
			.Where(j => j.Enabled && j.NextRunAt <= DateTimeOffset.UtcNow)
			.ToListAsync();

		Assert.Empty(dueJobs);
	}

	// ── Job Update After Execution ──────────────────────────────────────

	[Fact]
	public async Task UpdateJobAfterExecution_SetsLastRunAndNextRun() {
		ChatEntity chat = await this.SeedChatAsync();
		DateTimeOffset now = DateTimeOffset.UtcNow;

		CronJobEntity job = new() {
			Name = "test-job",
			CronExpression = "0 9 * * *",
			Handler = "test_handler",
			ChatId = chat.Id,
			Enabled = true,
			NextRunAt = now.AddMinutes(-1),
			CreatedAt = now,
			UpdatedAt = now
		};
		this._db.CronJobs.Add(job);
		await this._db.SaveChangesAsync();

		CronExpression cron = CronExpression.Parse(job.CronExpression);
		DateTimeOffset? nextRun = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

		job.LastRunAt = now;
		job.NextRunAt = nextRun ?? now.AddDays(1);
		job.UpdatedAt = now;
		await this._db.SaveChangesAsync();

		CronJobEntity? updated = await this._db.CronJobs.FindAsync(job.Id);
		Assert.NotNull(updated);
		Assert.NotNull(updated.LastRunAt);
		Assert.True(updated.NextRunAt > now);
	}

	// ── Handler Resolution ──────────────────────────────────────────────

	[Fact]
	public void ResolveHandler_KnownHandler_ReturnsHandler() {
		Mock<ICronHandler> handler = new();
		handler.Setup(h => h.HandlerName).Returns("test_handler");

		CronService service = new(
			this._scopeFactory.Object,
			[handler.Object],
			NullLogger<CronService>.Instance);

		ICronHandler? resolved = service.ResolveHandler("test_handler");

		Assert.NotNull(resolved);
		Assert.Equal("test_handler", resolved.HandlerName);
	}

	[Fact]
	public void ResolveHandler_UnknownHandler_ReturnsNull() {
		ICronHandler? resolved = this._sut.ResolveHandler("nonexistent");

		Assert.Null(resolved);
	}
}
