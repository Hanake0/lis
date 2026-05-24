namespace Lis.Api.Services;

/// <summary>
/// Pluggable dispatcher for a single multi-run target. Concrete implementations
/// decide how to inject the prompt into the target chat (usually via
/// <c>IConversationService</c>); the default <see cref="NoOpMultiRunDispatcher"/>
/// just reports success so the rest of the pipeline can be tested without
/// AI providers wired.
/// </summary>
public interface IMultiRunDispatcher {
	Task<(string status, string? error)> DispatchAsync(
		MultiRunTarget target,
		string         prompt,
		CancellationToken ct
	);
}

public sealed record MultiRunTarget(string ChatExternalId, string? AgentName, string? Model);

public sealed class NoOpMultiRunDispatcher :IMultiRunDispatcher {
	public Task<(string status, string? error)> DispatchAsync(
		MultiRunTarget target,
		string         prompt,
		CancellationToken ct
	) => Task.FromResult<(string, string?)>(("ok", null));
}
