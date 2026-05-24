using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Persistence;

public sealed class ScheduledTaskServiceTests :IDisposable {
	private readonly LisDbContext         db;
	private readonly FakeTimeProvider     time;
	private readonly ScheduledTaskService sut;

	public ScheduledTaskServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"tasks-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new ScheduledTaskService(this.db, this.time);
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

	// ── CreateAsync ────────────────────────────────────────────────

	[Fact]
	public async Task CreateAsync_Persists_Task_With_Computed_NextRun() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "every minute",
			CronExpression: "* * * * *",
			Prompt:         "do stuff"
		));

		Assert.NotEqual(0, task.Id);
		Assert.True(task.Enabled);
		Assert.NotNull(task.NextRunAt);
		Assert.True(task.NextRunAt > this.time.GetUtcNow());
		Assert.Equal(this.time.GetUtcNow(), task.CreatedAt);
	}

	[Fact]
	public async Task CreateAsync_Disabled_Task_Has_Null_NextRun() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "off",
			CronExpression: "* * * * *",
			Prompt:         "x",
			Enabled:        false
		));
		Assert.Null(task.NextRunAt);
	}

	[Fact]
	public async Task CreateAsync_Invalid_Cron_Throws_ArgumentException() {
		ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "bad",
			CronExpression: "not a cron",
			Prompt:         "x"
		)));
		Assert.Contains("cron_expr", ex.Message);
	}

	[Fact]
	public async Task CreateAsync_Empty_Name_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           " ",
			CronExpression: "* * * * *",
			Prompt:         "x"
		)));
	}

	[Fact]
	public async Task CreateAsync_Empty_Prompt_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "n",
			CronExpression: "* * * * *",
			Prompt:         " "
		)));
	}

	[Fact]
	public async Task CreateAsync_Invalid_Timezone_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "tz",
			CronExpression: "* * * * *",
			Prompt:         "x",
			Timezone:       "Mars/Olympus_Mons"
		)));
	}

	[Fact]
	public async Task CreateAsync_With_Project_Resolves_To_Project_Id() {
		ProjectEntity project = await this.SeedProjectAsync("/repo");

		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:              "scoped",
			CronExpression:    "* * * * *",
			Prompt:            "x",
			ProjectExternalId: project.ExternalId
		));

		Assert.Equal(project.Id, task.ProjectId);
	}

	[Fact]
	public async Task CreateAsync_With_Unknown_Project_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:              "x",
			CronExpression:    "* * * * *",
			Prompt:            "x",
			ProjectExternalId: "missing"
		)));
	}

	// ── UpdateAsync ────────────────────────────────────────────────

	[Fact]
	public async Task UpdateAsync_Partial_Update_Leaves_Untouched_Fields() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "orig",
			CronExpression: "* * * * *",
			Prompt:         "orig"
		));

		ScheduledTaskEntity updated = await this.sut.UpdateAsync(task.ExternalId, new UpdateScheduledTaskInput(
			Name: "renamed"
		));

		Assert.Equal("renamed",       updated.Name);
		Assert.Equal("* * * * *",     updated.CronExpression);
		Assert.Equal("orig",          updated.Prompt);
	}

	[Fact]
	public async Task UpdateAsync_Cron_Change_Recomputes_NextRun() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "n",
			CronExpression: "0 0 * * *",
			Prompt:         "x"
		));
		DateTimeOffset? originalNext = task.NextRunAt;

		ScheduledTaskEntity updated = await this.sut.UpdateAsync(task.ExternalId, new UpdateScheduledTaskInput(
			CronExpression: "* * * * *"
		));

		Assert.NotEqual(originalNext, updated.NextRunAt);
	}

	[Fact]
	public async Task UpdateAsync_Disable_Clears_NextRun() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:           "n",
			CronExpression: "* * * * *",
			Prompt:         "x"
		));

		ScheduledTaskEntity updated = await this.sut.UpdateAsync(task.ExternalId, new UpdateScheduledTaskInput(
			Enabled: false
		));

		Assert.False(updated.Enabled);
		Assert.Null(updated.NextRunAt);
	}

	[Fact]
	public async Task UpdateAsync_Empty_Project_Detaches() {
		ProjectEntity project = await this.SeedProjectAsync("/x");
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name:              "n",
			CronExpression:    "* * * * *",
			Prompt:            "x",
			ProjectExternalId: project.ExternalId
		));

		ScheduledTaskEntity updated = await this.sut.UpdateAsync(task.ExternalId, new UpdateScheduledTaskInput(
			ProjectExternalId: ""
		));

		Assert.Null(updated.ProjectId);
	}

	[Fact]
	public async Task UpdateAsync_Missing_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.UpdateAsync("nope", new UpdateScheduledTaskInput()));
	}

	// ── DeleteAsync ────────────────────────────────────────────────

	[Fact]
	public async Task DeleteAsync_Removes_Task() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name: "gone", CronExpression: "* * * * *", Prompt: "x"
		));

		await this.sut.DeleteAsync(task.ExternalId);

		Assert.Null(await this.db.ScheduledTasks.FirstOrDefaultAsync(t => t.ExternalId == task.ExternalId));
	}

	// ── MarkFiredAsync ─────────────────────────────────────────────

	[Fact]
	public async Task MarkFiredAsync_Updates_Status_And_Bumps_NextRun() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name: "n", CronExpression: "* * * * *", Prompt: "x"
		));
		DateTimeOffset? before = task.NextRunAt;

		this.time.Advance(TimeSpan.FromMinutes(1));
		ScheduledTaskEntity fired = await this.sut.MarkFiredAsync(task.Id, "ok", null);

		Assert.Equal("ok", fired.LastStatus);
		Assert.NotNull(fired.LastRunAt);
		Assert.Equal(this.time.GetUtcNow(), fired.LastRunAt);
		Assert.NotNull(fired.NextRunAt);
		Assert.True(fired.NextRunAt > before);
	}

	[Fact]
	public async Task MarkFiredAsync_Records_Error_When_Provided() {
		ScheduledTaskEntity task = await this.sut.CreateAsync(new CreateScheduledTaskInput(
			Name: "n", CronExpression: "* * * * *", Prompt: "x"
		));

		ScheduledTaskEntity fired = await this.sut.MarkFiredAsync(task.Id, "error", "boom");

		Assert.Equal("error", fired.LastStatus);
		Assert.Equal("boom",  fired.LastError);
	}

	// ── ListEnabledAsync / ListForProjectAsync ────────────────────

	[Fact]
	public async Task ListEnabledAsync_Returns_Only_Enabled() {
		await this.sut.CreateAsync(new CreateScheduledTaskInput(Name: "a", CronExpression: "* * * * *", Prompt: "x", Enabled: true));
		await this.sut.CreateAsync(new CreateScheduledTaskInput(Name: "b", CronExpression: "* * * * *", Prompt: "x", Enabled: false));

		IReadOnlyList<ScheduledTaskEntity> list = await this.sut.ListEnabledAsync();
		Assert.Single(list);
		Assert.Equal("a", list[0].Name);
	}

	[Fact]
	public async Task ListForProjectAsync_Filters_By_Project() {
		ProjectEntity project = await this.SeedProjectAsync("/x");
		await this.sut.CreateAsync(new CreateScheduledTaskInput(Name: "a", CronExpression: "* * * * *", Prompt: "x", ProjectExternalId: project.ExternalId));
		await this.sut.CreateAsync(new CreateScheduledTaskInput(Name: "b", CronExpression: "* * * * *", Prompt: "x"));

		IReadOnlyList<ScheduledTaskEntity> list = await this.sut.ListForProjectAsync(project.ExternalId);
		Assert.Single(list);
		Assert.Equal("a", list[0].Name);
	}

	[Fact]
	public async Task ListForProjectAsync_Unknown_Project_Throws() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.ListForProjectAsync("missing"));
	}

	// ── ComputeNextRun ─────────────────────────────────────────────

	[Fact]
	public void ComputeNextRun_Returns_Next_Occurrence_After_Reference() {
		DateTimeOffset reference = DateTimeOffset.Parse("2026-01-01T00:00:30Z");
		DateTimeOffset? next = this.sut.ComputeNextRun("* * * * *", null, reference);
		Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:01:00Z"), next);
	}

	[Fact]
	public void ComputeNextRun_Respects_Timezone() {
		// Sao Paulo is UTC-3 in standard time; 9 AM in SP = 12 PM UTC.
		DateTimeOffset reference = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
		DateTimeOffset? next = this.sut.ComputeNextRun("0 9 * * *", "E. South America Standard Time", reference);
		Assert.Equal(DateTimeOffset.Parse("2026-01-01T12:00:00Z"), next);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<ProjectEntity> SeedProjectAsync(string path) {
		ProjectService projects = new(this.db, this.time);
		return await projects.CreateAsync(path);
	}
}
