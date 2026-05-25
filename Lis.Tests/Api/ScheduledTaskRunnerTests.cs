using Lis.Api.Schemas.Events;
using Lis.Api.Services;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Integration tests for <see cref="ScheduledTaskRunner"/>. We build a real DI container
/// with EF Core InMemory + FakeTimeProvider + a CapturingEventBus + a CapturingExecutor,
/// start the runner, and drive time forward to verify it fires due tasks, publishes
/// SSE events, and respects CRUD notifications via the coordinator.
/// </summary>
public sealed class ScheduledTaskRunnerTests :IAsyncDisposable {
	private readonly ServiceProvider          services;
	private readonly FakeTimeProvider         time;
	private readonly CapturingEventBus        bus;
	private readonly CapturingExecutor        executor;
	private readonly ScheduledTaskCoordinator coordinator;
	private readonly ScheduledTaskRunner      runner;

	public ScheduledTaskRunnerTests() {
		ServiceCollection sc = new();
		this.time     = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus      = new CapturingEventBus();
		this.executor = new CapturingExecutor();

		string dbName = $"runner-{Guid.NewGuid()}";
		sc.AddSingleton<TimeProvider>(this.time);
		sc.AddSingleton<IEventBus>(this.bus);
		sc.AddScoped<IScheduledTaskExecutor>(_ => this.executor);
		sc.AddScoped<IScheduledTaskService, ScheduledTaskService>();
		// EF Core InMemory shares state across DbContext instances with the same database name,
		// so each scope gets a fresh context but all hit the same store.
		sc.AddScoped<LisDbContext>(_ => new TestDbContext(
			new DbContextOptionsBuilder<LisDbContext>()
				.UseInMemoryDatabase(dbName)
				.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
				.Options
		));
		sc.AddSingleton<ScheduledTaskCoordinator>(_ => new ScheduledTaskCoordinator(NullLogger<ScheduledTaskCoordinator>.Instance));
		sc.AddSingleton<ScheduledTaskRunner>(sp => new ScheduledTaskRunner(
			sp,
			sp.GetRequiredService<ScheduledTaskCoordinator>(),
			this.time,
			NullLogger<ScheduledTaskRunner>.Instance
		));

		this.services    = sc.BuildServiceProvider();
		this.coordinator = this.services.GetRequiredService<ScheduledTaskCoordinator>();
		this.runner      = this.services.GetRequiredService<ScheduledTaskRunner>();
	}

	public async ValueTask DisposeAsync() {
		await this.services.DisposeAsync();
		GC.SuppressFinalize(this);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	[Fact]
	public async Task Runner_Fires_Due_Task_On_Startup_Catch_Up() {
		// Seed a task whose next_run_at is in the past — runner should fire it immediately.
		using (IServiceScope scope = this.services.CreateScope()) {
			IScheduledTaskService svc = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
			ScheduledTaskEntity created = await svc.CreateAsync(new CreateScheduledTaskInput(
				Name: "due", CronExpression: "* * * * *", Prompt: "x"
			));
			// Force NextRunAt into the past
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
			ScheduledTaskEntity loaded = await db.ScheduledTasks.FirstAsync(t => t.Id == created.Id);
			loaded.NextRunAt = this.time.GetUtcNow().AddMinutes(-5);
			await db.SaveChangesAsync();
		}

		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
		Task running = this.runner.StartAsync(cts.Token);
		await this.WaitForExecutionAsync(min: 1, ct: cts.Token);

		await this.runner.StopAsync(default);
		await running;

		Assert.True(this.executor.Calls >= 1);
		Assert.Contains(this.bus.Captured, ev => ev.Name == ScheduledTaskEventNames.Run);
	}

	[Fact]
	public async Task Runner_Picks_Up_New_Task_Via_Coordinator_NotifyChanged() {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
		Task running = this.runner.StartAsync(cts.Token);

		// Wait until the runner has bound to the coordinator (StartAsync only awaits the kickoff).
		await Task.Delay(50, cts.Token);

		long taskId;
		using (IServiceScope scope = this.services.CreateScope()) {
			IScheduledTaskService svc = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
			ScheduledTaskEntity task = await svc.CreateAsync(new CreateScheduledTaskInput(
				Name: "new", CronExpression: "* * * * *", Prompt: "x"
			));
			taskId = task.Id;

			// Force NextRunAt into the immediate past so the runner fires it on the next loop iteration.
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
			ScheduledTaskEntity loaded = await db.ScheduledTasks.FirstAsync(t => t.Id == taskId);
			loaded.NextRunAt = this.time.GetUtcNow().AddSeconds(-1);
			await db.SaveChangesAsync();
		}

		await this.coordinator.NotifyChangedAsync(taskId, cts.Token);
		await this.WaitForExecutionAsync(min: 1, ct: cts.Token);

		await this.runner.StopAsync(default);
		await running;

		Assert.True(this.executor.Calls >= 1);
	}

	[Fact]
	public async Task Runner_With_Empty_Heap_Does_Not_Busy_Loop() {
		// Regression for the OOM observed in production: when the heap was empty,
		// the runner spun a tight loop allocating a fresh CancellationTokenSource
		// every microsecond (the empty-heap branch skipped `await Task.Delay` because
		// `Timeout.InfiniteTimeSpan > TimeSpan.Zero` is false, AND ClearWaiter was a
		// no-op so the coordinator's bag of CTSes grew unbounded). Observed growth
		// was ~400 MB/s. After the fix the loop is parked on `Task.Delay(Infinite, ct)`
		// and allocates ~nothing while idle.

		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
		Task running = this.runner.StartAsync(cts.Token);

		// Give the runner a beat to load (heap is empty — `ReloadAllAsync` finds 0 tasks)
		// and park on its first Task.Delay. After this we sample allocation under what
		// should be a fully idle workload.
		await Task.Delay(100, cts.Token);

		long before = GC.GetTotalAllocatedBytes(precise: true);
		await Task.Delay(500, cts.Token);
		long after  = GC.GetTotalAllocatedBytes(precise: true);
		long delta  = after - before;

		await this.runner.StopAsync(default);
		await running;

		// With the bug this was 100s of MB in half a second. After the fix it's
		// nanoseconds of work — let through 2 MB of slack for ambient test-runner noise.
		Assert.True(delta < 2_000_000,
			$"Idle runner allocated {delta:N0} bytes in 500ms — busy-loop regression suspected.");
	}

	[Fact]
	public async Task Coordinator_Wake_Is_Idempotent_When_No_Waiter_Is_Registered() {
		// The pre-fix coordinator stored waiters in a ConcurrentBag and walked the bag
		// to cancel — silently no-op when empty. The new code reads a single CTS field;
		// guarding the null case explicitly catches a future refactor that forgets it.
		this.coordinator.NotifyRemoved(taskId: 9999);
		this.coordinator.NotifyRemoved(taskId: 9999);
		await this.coordinator.NotifyChangedAsync(taskId: 9999, CancellationToken.None);
		// No assertion needed — passing means no NullReferenceException / no throw.
	}

	[Fact]
	public async Task Runner_Captures_Executor_Failure_As_Error_Event() {
		this.executor.ThrowOnNext = true;

		using (IServiceScope scope = this.services.CreateScope()) {
			IScheduledTaskService svc = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
			ScheduledTaskEntity created = await svc.CreateAsync(new CreateScheduledTaskInput(
				Name: "boom", CronExpression: "* * * * *", Prompt: "x"
			));
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
			ScheduledTaskEntity loaded = await db.ScheduledTasks.FirstAsync(t => t.Id == created.Id);
			loaded.NextRunAt = this.time.GetUtcNow().AddSeconds(-1);
			await db.SaveChangesAsync();
		}

		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
		Task running = this.runner.StartAsync(cts.Token);
		await this.WaitForExecutionAsync(min: 1, ct: cts.Token);

		await this.runner.StopAsync(default);
		await running;

		ServerEvent runEvent = Assert.Single(this.bus.Captured, ev => ev.Name == ScheduledTaskEventNames.Run);
		ScheduledTaskRunPayload payload = Assert.IsType<ScheduledTaskRunPayload>(runEvent.Data);
		Assert.Equal("error", payload.Status);
		Assert.Equal("boom",  payload.Error);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task WaitForExecutionAsync(int min, CancellationToken ct) {
		// Poll until the executor has run at least `min` times, capped by ct.
		while (!ct.IsCancellationRequested && this.executor.Calls < min) {
			await Task.Delay(25, ct);
		}
	}

	private sealed class CapturingExecutor :IScheduledTaskExecutor {
		public int  Calls       { get; private set; }
		public bool ThrowOnNext { get; set; }

		public Task<(string status, string? error)> ExecuteAsync(ScheduledTaskEntity task, CancellationToken ct) {
			this.Calls++;
			if (this.ThrowOnNext) {
				this.ThrowOnNext = false;
				throw new InvalidOperationException("boom");
			}
			return Task.FromResult<(string, string?)>(("ok", null));
		}
	}

	private sealed class CapturingEventBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			lock (this.Captured) this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256, Func<ServerEvent, bool>? filter = null) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}
}
