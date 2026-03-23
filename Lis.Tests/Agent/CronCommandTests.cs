using Lis.Agent.Commands;
using Lis.Core.Channel;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lis.Tests.Agent;

public class CronCommandTests : IDisposable {
	private readonly LisDbContext _db;
	private readonly CronCommand _sut;

	public CronCommandTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this._db = new TestDbContext(options);
		this._sut = new CronCommand();
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

	private async Task<(ChatEntity Chat, AgentEntity Agent)> SeedDataAsync() {
		AgentEntity agent = new() {
			Name = "default", DisplayName = "Lis", Model = "test-model", IsDefault = true,
			CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
		};
		this._db.Agents.Add(agent);
		await this._db.SaveChangesAsync();

		ChatEntity chat = new() {
			ExternalId = "test-chat@jid",
			Name = "Test Chat",
			Enabled = true,
			AgentId = agent.Id,
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		this._db.Chats.Add(chat);
		await this._db.SaveChangesAsync();

		return (chat, agent);
	}

	private CommandContext CreateContext(ChatEntity chat, AgentEntity agent, string? args) {
		IncomingMessage msg = new() {
			ExternalId = "m1",
			ChatId = chat.ExternalId,
			SenderId = "owner@jid",
			Body = args is not null ? $"/cron {args}" : "/cron"
		};
		return new CommandContext(msg, chat, null, this._db, agent, args);
	}

	// ── Triggers ────────────────────────────────────────────────────────

	[Fact]
	public void Triggers_ContainsCron() {
		Assert.Contains("/cron", this._sut.Triggers);
	}

	[Fact]
	public void OwnerOnly_IsTrue() {
		Assert.True(this._sut.OwnerOnly);
	}

	// ── No Args ─────────────────────────────────────────────────────────

	[Fact]
	public async Task Execute_NoArgs_ReturnsUsage() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, null);

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("Usage", result, StringComparison.OrdinalIgnoreCase);
	}

	// ── Add ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task Execute_Add_ValidCron_CreatesJob() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "add \"*/5 * * * *\" test_handler My Test Job");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("created", result, StringComparison.OrdinalIgnoreCase);

		CronJobEntity? job = await this._db.CronJobs.FirstOrDefaultAsync();
		Assert.NotNull(job);
		Assert.Equal("My Test Job", job.Name);
		Assert.Equal("*/5 * * * *", job.CronExpression);
		Assert.Equal("test_handler", job.Handler);
		Assert.Equal(chat.Id, job.ChatId);
		Assert.True(job.Enabled);
	}

	[Fact]
	public async Task Execute_Add_InvalidCron_ReturnsError() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "add \"invalid\" test_handler My Job");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("invalid", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Execute_Add_MissingArgs_ReturnsUsage() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "add");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("Usage", result, StringComparison.OrdinalIgnoreCase);
	}

	// ── List ────────────────────────────────────────────────────────────

	[Fact]
	public async Task Execute_List_NoJobs_ReturnsEmpty() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "list");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("no cron jobs", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Execute_List_WithJobs_ListsThem() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();

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

		CommandContext ctx = this.CreateContext(chat, agent, "list");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("Daily Backup", result);
		Assert.Contains("0 2 * * *", result);
	}

	// ── Remove ──────────────────────────────────────────────────────────

	[Fact]
	public async Task Execute_Remove_ExistingJob_RemovesIt() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();

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

		CommandContext ctx = this.CreateContext(chat, agent, $"remove {job.Id}");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("removed", result, StringComparison.OrdinalIgnoreCase);
		Assert.Null(await this._db.CronJobs.FindAsync(job.Id));
	}

	[Fact]
	public async Task Execute_Remove_NonExistentJob_ReturnsNotFound() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "remove 9999");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Execute_Remove_InvalidId_ReturnsError() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "remove abc");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("invalid", result, StringComparison.OrdinalIgnoreCase);
	}

	// ── Unknown Subcommand ──────────────────────────────────────────────

	[Fact]
	public async Task Execute_UnknownSubcommand_ReturnsUsage() {
		(ChatEntity chat, AgentEntity agent) = await this.SeedDataAsync();
		CommandContext ctx = this.CreateContext(chat, agent, "unknown");

		string result = await this._sut.ExecuteAsync(ctx, CancellationToken.None);

		Assert.Contains("Usage", result, StringComparison.OrdinalIgnoreCase);
	}
}
