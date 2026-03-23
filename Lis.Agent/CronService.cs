using Cronos;

using Lis.Core.Channel;
using Lis.Core.Cron;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lis.Agent;

public sealed partial class CronService(
	IServiceScopeFactory       scopeFactory,
	IEnumerable<ICronHandler>  handlers,
	ILogger<CronService>       logger) : IHostedService, IDisposable {

	private Timer? _timer;
	private readonly Dictionary<string, ICronHandler> _handlers =
		handlers.ToDictionary(h => h.HandlerName, StringComparer.OrdinalIgnoreCase);

	private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

	[Trace("CronService > StartAsync")]
	public async Task StartAsync(CancellationToken ct) {
		LogStarting(logger, this._handlers.Count);

		await this.InitializeNextRunTimesAsync(ct);

		this._timer = new Timer(
			callback: _ => Task.Run(() => this.TickAsync(CancellationToken.None)),
			state: null,
			dueTime: TickInterval,
			period: TickInterval);
	}

	public Task StopAsync(CancellationToken ct) {
		LogStopping(logger);
		this._timer?.Change(Timeout.Infinite, 0);
		return Task.CompletedTask;
	}

	public void Dispose() {
		this._timer?.Dispose();
	}

	/// <summary>Resolve a handler by name. Used for testing.</summary>
	public ICronHandler? ResolveHandler(string handlerName) {
		return this._handlers.GetValueOrDefault(handlerName);
	}

	[Trace("CronService > InitializeNextRunTimesAsync")]
	private async Task InitializeNextRunTimesAsync(CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<CronJobEntity> jobs = await db.CronJobs
			.Where(j => j.Enabled)
			.ToListAsync(ct);

		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (CronJobEntity job in jobs) {
			try {
				CronExpression cron = CronExpression.Parse(job.CronExpression);
				DateTimeOffset? next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);
				if (next is not null) {
					job.NextRunAt = next.Value;
					job.UpdatedAt = now;
				}
			} catch (Exception ex) {
				LogCronParseError(logger, job.Id, job.CronExpression, ex);
			}
		}

		await db.SaveChangesAsync(ct);
		LogInitialized(logger, jobs.Count);
	}

	[Trace("CronService > TickAsync")]
	private async Task TickAsync(CancellationToken ct) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			DateTimeOffset now = DateTimeOffset.UtcNow;
			List<CronJobEntity> dueJobs = await db.CronJobs
				.Include(j => j.Chat)
				.Where(j => j.Enabled && j.NextRunAt <= now)
				.ToListAsync(ct);

			if (dueJobs.Count == 0) return;

			LogDueJobs(logger, dueJobs.Count);

			IChannelClient? channelClient = scope.ServiceProvider.GetService<IChannelClient>();

			foreach (CronJobEntity job in dueJobs) {
				try {
					await this.ExecuteJobAsync(job, channelClient, ct);

					CronExpression cron = CronExpression.Parse(job.CronExpression);
					DateTimeOffset? next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

					job.LastRunAt = DateTimeOffset.UtcNow;
					job.NextRunAt = next ?? DateTimeOffset.UtcNow.AddDays(1);
					job.UpdatedAt = DateTimeOffset.UtcNow;
				} catch (Exception ex) {
					LogJobExecutionError(logger, job.Id, job.Name, ex);
				}
			}

			await db.SaveChangesAsync(ct);
		} catch (Exception ex) {
			LogTickError(logger, ex);
		}
	}

	[Trace("CronService > ExecuteJobAsync")]
	private async Task ExecuteJobAsync(CronJobEntity job, IChannelClient? channelClient, CancellationToken ct) {
		LogExecutingJob(logger, job.Id, job.Name, job.Handler);

		if (!this._handlers.TryGetValue(job.Handler, out ICronHandler? handler)) {
			LogNoHandler(logger, job.Handler, job.Id);

			if (channelClient is not null) {
				await channelClient.SendMessageAsync(
					job.Chat.ExternalId,
					$"⚠️ Cron job '{job.Name}' failed: no handler registered for '{job.Handler}'.",
					ct: ct);
			}
			return;
		}

		string? result = await handler.ExecuteAsync(job.ChatId, ct);

		if (result is { Length: > 0 } && channelClient is not null) {
			await channelClient.SendMessageAsync(job.Chat.ExternalId, result, ct: ct);
		}
	}

	// ── Log Messages ────────────────────────────────────────────────────

	[LoggerMessage(Level = LogLevel.Information, Message = "CronService starting — {handlerCount} handlers registered")]
	private static partial void LogStarting(ILogger logger, int handlerCount);

	[LoggerMessage(Level = LogLevel.Information, Message = "CronService stopping")]
	private static partial void LogStopping(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "Initialized next-run times for {count} jobs")]
	private static partial void LogInitialized(ILogger logger, int count);

	[LoggerMessage(Level = LogLevel.Information, Message = "Found {count} due cron jobs")]
	private static partial void LogDueJobs(ILogger logger, int count);

	[LoggerMessage(Level = LogLevel.Information, Message = "Executing cron job {jobId} ({name}) — handler: {handler}")]
	private static partial void LogExecutingJob(ILogger logger, long jobId, string name, string handler);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse cron expression for job {jobId}: {expression}")]
	private static partial void LogCronParseError(ILogger logger, long jobId, string expression, Exception ex);

	[LoggerMessage(Level = LogLevel.Warning, Message = "No handler registered for '{handler}' (job {jobId})")]
	private static partial void LogNoHandler(ILogger logger, string handler, long jobId);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to execute cron job {jobId} ({name})")]
	private static partial void LogJobExecutionError(ILogger logger, long jobId, string name, Exception ex);

	[LoggerMessage(Level = LogLevel.Error, Message = "CronService tick failed")]
	private static partial void LogTickError(ILogger logger, Exception ex);
}
