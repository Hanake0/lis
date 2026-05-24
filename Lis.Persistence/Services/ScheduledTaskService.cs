using Cronos;

using Lis.Core.Util;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence.Services;

/// <summary>
/// CRUD for <see cref="ScheduledTaskEntity"/> plus cron parsing and
/// <c>next_run_at</c> computation. The actual firing lives in the
/// <c>ScheduledTaskRunner</c> hosted service in <c>Lis.Api</c>.
/// </summary>
public interface IScheduledTaskService {
	Task<IReadOnlyList<ScheduledTaskEntity>> ListEnabledAsync(CancellationToken ct = default);
	Task<IReadOnlyList<ScheduledTaskEntity>> ListForProjectAsync(string projectExternalId, CancellationToken ct = default);
	Task<ScheduledTaskEntity?>               GetByExternalIdAsync(string externalId, CancellationToken ct = default);
	Task<ScheduledTaskEntity>                CreateAsync(CreateScheduledTaskInput input, CancellationToken ct = default);
	Task<ScheduledTaskEntity>                UpdateAsync(string externalId, UpdateScheduledTaskInput input, CancellationToken ct = default);
	Task                                     DeleteAsync(string externalId, CancellationToken ct = default);

	/// <summary>Mark a task as fired, persisting last_run_at/last_status/last_error and bumping next_run_at.</summary>
	Task<ScheduledTaskEntity>                MarkFiredAsync(long id, string status, string? error, CancellationToken ct = default);

	/// <summary>Compute the next occurrence after <paramref name="afterUtc"/> for <paramref name="cronExpr"/>.</summary>
	DateTimeOffset? ComputeNextRun(string cronExpr, string? timezone, DateTimeOffset afterUtc);
}

/// <summary>Inputs gathered from REST surface — all required fields filled.</summary>
public sealed record CreateScheduledTaskInput(
	string  Name,
	string  CronExpression,
	string  Prompt,
	string? ProjectExternalId = null,
	string? AgentName         = null,
	string? Model             = null,
	string? Timezone          = null,
	bool    Enabled           = true
);

/// <summary>Partial update — null fields are left untouched.</summary>
public sealed record UpdateScheduledTaskInput(
	string? Name             = null,
	string? CronExpression   = null,
	string? Prompt           = null,
	string? ProjectExternalId = null,
	string? AgentName         = null,
	string? Model             = null,
	string? Timezone          = null,
	bool?   Enabled           = null
);

public sealed class ScheduledTaskService(LisDbContext db, TimeProvider time) :IScheduledTaskService {

	[Trace("ScheduledTaskService > ListEnabledAsync")]
	public async Task<IReadOnlyList<ScheduledTaskEntity>> ListEnabledAsync(CancellationToken ct = default) =>
		await db.ScheduledTasks
				.AsNoTracking()
				.Where(t => t.Enabled)
				.OrderBy(t => t.NextRunAt)
				.ToListAsync(ct);

	[Trace("ScheduledTaskService > ListForProjectAsync")]
	public async Task<IReadOnlyList<ScheduledTaskEntity>> ListForProjectAsync(string projectExternalId, CancellationToken ct = default) {
		ProjectEntity? project = await db.Projects.FirstOrDefaultAsync(p => p.ExternalId == projectExternalId, ct)
			?? throw new KeyNotFoundException($"Project '{projectExternalId}' not found");

		return await db.ScheduledTasks
					   .AsNoTracking()
					   .Where(t => t.ProjectId == project.Id)
					   .OrderBy(t => t.NextRunAt)
					   .ToListAsync(ct);
	}

	[Trace("ScheduledTaskService > GetByExternalIdAsync")]
	public Task<ScheduledTaskEntity?> GetByExternalIdAsync(string externalId, CancellationToken ct = default) =>
		db.ScheduledTasks.FirstOrDefaultAsync(t => t.ExternalId == externalId, ct);

	[Trace("ScheduledTaskService > CreateAsync")]
	public async Task<ScheduledTaskEntity> CreateAsync(CreateScheduledTaskInput input, CancellationToken ct = default) {
		ValidateOrThrow(input.Name, input.CronExpression, input.Prompt, input.Timezone);

		long? projectId = await this.ResolveProjectIdAsync(input.ProjectExternalId, ct);

		DateTimeOffset now = time.GetUtcNow();
		ScheduledTaskEntity task = new() {
			ExternalId     = Guid.NewGuid().ToString("N")[..16],
			ProjectId      = projectId,
			Name           = input.Name.Trim(),
			CronExpression = input.CronExpression.Trim(),
			Prompt         = input.Prompt,
			AgentName      = input.AgentName,
			Model          = input.Model,
			Timezone       = input.Timezone,
			Enabled        = input.Enabled,
			NextRunAt      = input.Enabled ? this.ComputeNextRun(input.CronExpression, input.Timezone, now) : null,
			CreatedAt      = now,
			UpdatedAt      = now,
		};
		db.ScheduledTasks.Add(task);
		await db.SaveChangesAsync(ct);
		return task;
	}

	[Trace("ScheduledTaskService > UpdateAsync")]
	public async Task<ScheduledTaskEntity> UpdateAsync(string externalId, UpdateScheduledTaskInput input, CancellationToken ct = default) {
		ScheduledTaskEntity task = await this.RequireAsync(externalId, ct);

		if (input.Name             is not null) task.Name           = input.Name.Trim();
		if (input.CronExpression   is not null) task.CronExpression = input.CronExpression.Trim();
		if (input.Prompt           is not null) task.Prompt         = input.Prompt;
		if (input.AgentName        is not null) task.AgentName      = input.AgentName;
		if (input.Model            is not null) task.Model          = input.Model;
		if (input.Timezone         is not null) task.Timezone       = input.Timezone;
		if (input.Enabled          is not null) task.Enabled        = input.Enabled.Value;
		if (input.ProjectExternalId is not null) {
			// Pass empty string to detach explicitly.
			task.ProjectId = input.ProjectExternalId.Length == 0
				? null
				: await this.ResolveProjectIdAsync(input.ProjectExternalId, ct);
		}

		ValidateOrThrow(task.Name, task.CronExpression, task.Prompt, task.Timezone);

		DateTimeOffset now = time.GetUtcNow();
		task.NextRunAt = task.Enabled ? this.ComputeNextRun(task.CronExpression, task.Timezone, now) : null;
		task.UpdatedAt = now;

		await db.SaveChangesAsync(ct);
		return task;
	}

	[Trace("ScheduledTaskService > DeleteAsync")]
	public async Task DeleteAsync(string externalId, CancellationToken ct = default) {
		ScheduledTaskEntity task = await this.RequireAsync(externalId, ct);
		db.ScheduledTasks.Remove(task);
		await db.SaveChangesAsync(ct);
	}

	[Trace("ScheduledTaskService > MarkFiredAsync")]
	public async Task<ScheduledTaskEntity> MarkFiredAsync(long id, string status, string? error, CancellationToken ct = default) {
		ScheduledTaskEntity task = await db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
			?? throw new KeyNotFoundException($"Scheduled task '{id}' not found");

		DateTimeOffset now = time.GetUtcNow();
		task.LastRunAt  = now;
		task.LastStatus = status;
		task.LastError  = error;
		task.UpdatedAt  = now;

		DateTimeOffset? next = task.Enabled ? this.ComputeNextRun(task.CronExpression, task.Timezone, now) : null;
		if (next is null && task.Enabled) {
			// Cron expression that never fires again — auto-disable so the heap stops carrying it.
			task.Enabled = false;
		}
		task.NextRunAt = next;

		await db.SaveChangesAsync(ct);
		return task;
	}

	public DateTimeOffset? ComputeNextRun(string cronExpr, string? timezone, DateTimeOffset afterUtc) {
		CronExpression expression = CronExpression.Parse(cronExpr.Trim());
		TimeZoneInfo tz = string.IsNullOrWhiteSpace(timezone)
			? TimeZoneInfo.Utc
			: TimeZoneInfo.FindSystemTimeZoneById(timezone);
		// Cronos returns the next occurrence STRICTLY AFTER the reference instant.
		return expression.GetNextOccurrence(afterUtc, tz);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<long?> ResolveProjectIdAsync(string? projectExternalId, CancellationToken ct) {
		if (string.IsNullOrEmpty(projectExternalId)) return null;
		ProjectEntity? project = await db.Projects.FirstOrDefaultAsync(p => p.ExternalId == projectExternalId, ct)
			?? throw new KeyNotFoundException($"Project '{projectExternalId}' not found");
		return project.Id;
	}

	private async Task<ScheduledTaskEntity> RequireAsync(string externalId, CancellationToken ct) =>
		await db.ScheduledTasks.FirstOrDefaultAsync(t => t.ExternalId == externalId, ct)
			?? throw new KeyNotFoundException($"Scheduled task '{externalId}' not found");

	private static void ValidateOrThrow(string name, string cronExpr, string prompt, string? timezone) {
		if (string.IsNullOrWhiteSpace(name))     throw new ArgumentException("name is required",     nameof(name));
		if (string.IsNullOrWhiteSpace(cronExpr)) throw new ArgumentException("cron_expr is required", nameof(cronExpr));
		if (string.IsNullOrWhiteSpace(prompt))   throw new ArgumentException("prompt is required",    nameof(prompt));

		// Validate the cron expression eagerly so invalid input never makes it into the heap.
		try { CronExpression.Parse(cronExpr.Trim()); }
		catch (CronFormatException ex) { throw new ArgumentException($"cron_expr invalid: {ex.Message}", nameof(cronExpr), ex); }

		if (timezone is not null) {
			try { TimeZoneInfo.FindSystemTimeZoneById(timezone); }
			catch (TimeZoneNotFoundException ex) { throw new ArgumentException($"timezone invalid: {ex.Message}", nameof(timezone), ex); }
		}
	}
}
