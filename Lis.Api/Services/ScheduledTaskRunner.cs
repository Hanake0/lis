using System.Collections.Concurrent;

using Lis.Api.Schemas.Events;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Services;

/// <summary>
/// In-process scheduler for <see cref="ScheduledTaskEntity"/>. Maintains a min-heap
/// keyed by <c>next_run_at</c> so the main loop sleeps until exactly the next due
/// task — no DB polling, which is critical for Neon serverless (it scales to zero
/// when idle and bills by compute time).
///
/// CRUD is communicated via <see cref="IEventBus"/>: when a task is created /
/// updated / deleted, the corresponding controller publishes an event (or directly
/// calls <see cref="RefreshAsync"/> via the registered <see cref="ScheduledTaskCoordinator"/>),
/// which rebuilds the heap entry and wakes the loop.
///
/// On startup, any task with <c>next_run_at &lt;= now</c> is fired immediately
/// (one catch-up per task — we don't replay every missed slot).
/// </summary>
public sealed partial class ScheduledTaskRunner(
	IServiceProvider               services,
	ScheduledTaskCoordinator       coordinator,
	TimeProvider                   time,
	ILogger<ScheduledTaskRunner>   logger
) :BackgroundService {

	/// <summary>Min-heap by NextRunAt then task id (for stable ordering of equal timestamps).</summary>
	private readonly SortedSet<HeapEntry> heap = new(HeapEntryComparer.Instance);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		// One load query at startup. After this the heap is the source of truth.
		await this.ReloadAllAsync(stoppingToken);
		coordinator.Bind(this);

		try {
			while (!stoppingToken.IsCancellationRequested) {
				CancellationTokenSource wakeOnChange = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
				coordinator.RegisterWaiter(wakeOnChange);

				try {
					TimeSpan wait = this.ComputeWait();
					if (wait > TimeSpan.Zero) await Task.Delay(wait, wakeOnChange.Token);
				} catch (OperationCanceledException) {
					// Either we're shutting down, or a CRUD event fired — loop will recompute.
					if (stoppingToken.IsCancellationRequested) break;
				} finally {
					coordinator.ClearWaiter(wakeOnChange);
				}

				await this.FireDueAsync(stoppingToken);
			}
		} finally {
			coordinator.Bind(null);
		}
	}

	// ── Public surface used by the coordinator ─────────────────────

	/// <summary>Re-add or update the task in the heap; called when controllers mutate a task.</summary>
	internal async Task RefreshAsync(long taskId, CancellationToken ct) {
		// Pop any existing entry for this id, then re-add from the DB.
		lock (this.heap) {
			HeapEntry? existing = this.heap.FirstOrDefault(e => e.TaskId == taskId);
			if (existing is not null) this.heap.Remove(existing);
		}

		using IServiceScope scope = services.CreateScope();
		LisDbContextResolver db = new(scope);
		ScheduledTaskEntity? task = await db.GetTaskByIdAsync(taskId, ct);
		if (task is null || !task.Enabled || task.NextRunAt is null) return;

		lock (this.heap) this.heap.Add(new HeapEntry(task.Id, task.NextRunAt.Value));
	}

	internal void Remove(long taskId) {
		lock (this.heap) {
			HeapEntry? existing = this.heap.FirstOrDefault(e => e.TaskId == taskId);
			if (existing is not null) this.heap.Remove(existing);
		}
	}

	internal Task ReloadAllAsync(CancellationToken ct) => this.ReloadAllInternalAsync(ct);

	// ── Internal ───────────────────────────────────────────────────

	private async Task ReloadAllInternalAsync(CancellationToken ct) {
		using IServiceScope scope = services.CreateScope();
		IScheduledTaskService svc = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
		IReadOnlyList<ScheduledTaskEntity> tasks = await svc.ListEnabledAsync(ct);

		lock (this.heap) {
			this.heap.Clear();
			foreach (ScheduledTaskEntity task in tasks) {
				if (task.NextRunAt is null) continue;
				this.heap.Add(new HeapEntry(task.Id, task.NextRunAt.Value));
			}
		}

		LogStartupLoad(logger, tasks.Count);
	}

	private TimeSpan ComputeWait() {
		HeapEntry? next;
		lock (this.heap) next = this.heap.Count == 0 ? null : this.heap.Min;
		if (next is null) return Timeout.InfiniteTimeSpan;

		TimeSpan delta = next.NextRunAt - time.GetUtcNow();
		return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
	}

	private async Task FireDueAsync(CancellationToken ct) {
		List<HeapEntry> due;
		DateTimeOffset now = time.GetUtcNow();
		lock (this.heap) {
			due = this.heap.Where(e => e.NextRunAt <= now).ToList();
			foreach (HeapEntry e in due) this.heap.Remove(e);
		}

		foreach (HeapEntry entry in due) {
			if (ct.IsCancellationRequested) break;
			await this.FireOneAsync(entry.TaskId, ct);
		}
	}

	private async Task FireOneAsync(long taskId, CancellationToken ct) {
		using IServiceScope scope = services.CreateScope();
		IScheduledTaskService svc        = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
		IScheduledTaskExecutor executor  = scope.ServiceProvider.GetRequiredService<IScheduledTaskExecutor>();
		IEventBus bus                    = scope.ServiceProvider.GetRequiredService<IEventBus>();

		LisDbContextResolver db = new(scope);
		ScheduledTaskEntity? task = await db.GetTaskByIdAsync(taskId, ct);
		if (task is null || !task.Enabled) return;

		string  status = "ok";
		string? error  = null;
		try {
			(status, error) = await executor.ExecuteAsync(task, ct);
		} catch (OperationCanceledException) { throw; }
		catch (Exception ex) {
			status = "error";
			error  = ex.Message;
			LogTaskFailed(logger, ex, task.Id, task.Name);
		}

		ScheduledTaskEntity updated = await svc.MarkFiredAsync(task.Id, status, error, ct);
		await bus.PublishAsync(new ServerEvent(
			0,
			ScheduledTaskEventNames.Run,
			new ScheduledTaskRunPayload(updated.ExternalId, status, error),
			time.GetUtcNow()
		), ct);

		// Push the task back onto the heap with the new NextRunAt.
		if (updated.Enabled && updated.NextRunAt is not null) {
			lock (this.heap) this.heap.Add(new HeapEntry(updated.Id, updated.NextRunAt.Value));
		}
	}

	// ── Heap entry ─────────────────────────────────────────────────

	private sealed record HeapEntry(long TaskId, DateTimeOffset NextRunAt);

	private sealed class HeapEntryComparer :IComparer<HeapEntry> {
		public static readonly HeapEntryComparer Instance = new();
		public int Compare(HeapEntry? x, HeapEntry? y) {
			if (x is null) return y is null ? 0 : -1;
			if (y is null) return 1;
			int byTime = x.NextRunAt.CompareTo(y.NextRunAt);
			return byTime != 0 ? byTime : x.TaskId.CompareTo(y.TaskId);
		}
	}

	// Avoid creating LisDbContextOptions plumbing in the runner — just resolve one
	// from the scope and use it to grab the task by id.
	private sealed class LisDbContextResolver(IServiceScope scope) {
		public async Task<ScheduledTaskEntity?> GetTaskByIdAsync(long taskId, CancellationToken ct) {
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
			return await db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
		}
	}

	// ── Logging source generators (avoid CA1873 boxing on hot path) ────

	[LoggerMessage(EventId = 100, Level = LogLevel.Information,
		Message = "ScheduledTaskRunner loaded {Count} enabled task(s) at startup")]
	private static partial void LogStartupLoad(ILogger logger, int count);

	[LoggerMessage(EventId = 101, Level = LogLevel.Error,
		Message = "Scheduled task {TaskId} ({Name}) failed")]
	private static partial void LogTaskFailed(ILogger logger, Exception ex, long taskId, string name);
}

/// <summary>
/// Decouples the runner from the controllers: controllers call
/// <see cref="NotifyChangedAsync"/> after a CRUD operation, and the runner
/// (when wired) picks up the change without holding a reference to the
/// controller layer.
/// </summary>
public sealed partial class ScheduledTaskCoordinator(ILogger<ScheduledTaskCoordinator> logger) {
	private ScheduledTaskRunner? runner;
	private readonly ConcurrentBag<CancellationTokenSource> waiters = [];

	internal void Bind(ScheduledTaskRunner? r) => this.runner = r;

	internal void RegisterWaiter(CancellationTokenSource cts) => this.waiters.Add(cts);
	internal void ClearWaiter(CancellationTokenSource cts) { /* GC-friendly no-op — the bag is recreated on each loop */ }

	public Task NotifyChangedAsync(long taskId, CancellationToken ct) {
		if (this.runner is null) {
			LogNoRunner(logger, taskId);
			return Task.CompletedTask;
		}
		Task refresh = this.runner.RefreshAsync(taskId, ct);
		this.WakeAll();
		return refresh;
	}

	[LoggerMessage(EventId = 200, Level = LogLevel.Debug,
		Message = "Coordinator notified for task {TaskId} but no runner is bound yet")]
	private static partial void LogNoRunner(ILogger logger, long taskId);

	public void NotifyRemoved(long taskId) {
		this.runner?.Remove(taskId);
		this.WakeAll();
	}

	public Task ReloadAllAsync(CancellationToken ct) {
		Task t = this.runner?.ReloadAllAsync(ct) ?? Task.CompletedTask;
		this.WakeAll();
		return t;
	}

	private void WakeAll() {
		// Snapshot then dispose — cancelling triggers the Task.Delay in the runner.
		while (this.waiters.TryTake(out CancellationTokenSource? cts)) {
			try { cts.Cancel(); } catch (ObjectDisposedException) { }
		}
	}
}
