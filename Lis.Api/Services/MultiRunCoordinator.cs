using Lis.Api.Schemas.Events;
using Lis.Core.Events;

namespace Lis.Api.Services;

/// <summary>
/// Drives the multi-run lifecycle: assigns a run id, dispatches each target on
/// the thread pool, and publishes a <c>multi_run_progress</c> SSE event for each
/// target completion. Runs are ephemeral — no DB persistence.
/// </summary>
public interface IMultiRunCoordinator {
	/// <summary>Start a run; returns the run id immediately while targets execute in the background.</summary>
	string Start(string prompt, IReadOnlyList<MultiRunTarget> targets);
}

public sealed class MultiRunCoordinator(
	IServiceProvider services,
	IEventBus        events,
	TimeProvider     time
) :IMultiRunCoordinator {

	public string Start(string prompt, IReadOnlyList<MultiRunTarget> targets) {
		ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
		ArgumentNullException.ThrowIfNull(targets);
		if (targets.Count == 0) throw new ArgumentException("targets is empty", nameof(targets));

		string runId = Guid.NewGuid().ToString("N")[..16];

		// Snapshot the dispatcher kind once so a swap mid-run can't change behaviour.
		_ = Task.Run(async () => {
			foreach (MultiRunTarget target in targets) {
				await this.DispatchOneAsync(runId, prompt, target);
			}
		});

		return runId;
	}

	private async Task DispatchOneAsync(string runId, string prompt, MultiRunTarget target) {
		string  status = "ok";
		string? error  = null;
		try {
			using IServiceScope scope = services.CreateScope();
			IMultiRunDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IMultiRunDispatcher>();
			(status, error) = await dispatcher.DispatchAsync(target, prompt, CancellationToken.None);
		} catch (Exception ex) {
			status = "error";
			error  = ex.Message;
		}

		await events.PublishAsync(new ServerEvent(
			0,
			MultiRunEventNames.Progress,
			new MultiRunProgressPayload(runId, target.ChatExternalId, status, error),
			time.GetUtcNow()
		));
	}
}
