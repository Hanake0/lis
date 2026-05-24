using Lis.Persistence.Entities;

namespace Lis.Api.Services;

/// <summary>
/// Pluggable executor for scheduled tasks. The <see cref="ScheduledTaskRunner"/>
/// invokes this when a task is due; concrete implementations decide what "firing"
/// means — e.g. inject the prompt into a chat via <c>IConversationService</c>,
/// hit a webhook, or just log.
/// </summary>
public interface IScheduledTaskExecutor {
	/// <summary>
	/// Execute the task. Return value pair is <c>(status, error)</c>.
	/// Status is a short string ("ok", "error", "skipped"); error is null on success.
	/// </summary>
	Task<(string status, string? error)> ExecuteAsync(ScheduledTaskEntity task, CancellationToken ct);
}

/// <summary>Default executor — records that the task fired but performs no work.
/// Wire a concrete executor (e.g. one that calls <c>IConversationService</c>) to
/// actually act on the prompt.</summary>
public sealed class NoOpScheduledTaskExecutor :IScheduledTaskExecutor {
	public Task<(string status, string? error)> ExecuteAsync(ScheduledTaskEntity task, CancellationToken ct) =>
		Task.FromResult<(string, string?)>(("ok", null));
}
