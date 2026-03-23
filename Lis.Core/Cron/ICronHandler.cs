namespace Lis.Core.Cron;

/// <summary>
/// Handles execution of a cron job. Implementations are resolved by handler name.
/// </summary>
public interface ICronHandler {
	/// <summary>Unique name used to match cron_job.handler column.</summary>
	string HandlerName { get; }

	/// <summary>
	/// Execute the cron job.
	/// Returns a message to send to the chat, or null if no message is needed.
	/// For non-deterministic jobs, the returned string is a prompt for the AI.
	/// </summary>
	Task<string?> ExecuteAsync(long chatId, CancellationToken ct);
}
