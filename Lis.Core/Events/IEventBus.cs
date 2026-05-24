using System.Threading.Channels;

namespace Lis.Core.Events;

/// <summary>
/// In-process pub/sub for cross-client SSE notifications.
/// Publishers call <see cref="PublishAsync"/> after a state mutation; subscribers
/// (typically the <c>/api/events</c> controller) read events from the channel returned
/// by <see cref="Subscribe"/>. Each subscription gets its own bounded queue so a slow
/// reader can't fan out backpressure onto producers.
///
/// Events also land in a ring buffer keyed by their <see cref="ServerEvent.Id"/> so
/// reconnecting clients can pass <c>Last-Event-ID</c> and replay anything they missed.
/// </summary>
public interface IEventBus {
	/// <summary>Publish an event to all current subscribers and append to the replay buffer.</summary>
	ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default);

	/// <summary>
	/// Subscribe for live events. Returns a channel reader that yields events as they
	/// are published; the caller is responsible for completing the subscription via
	/// the returned <see cref="IDisposable"/> when the connection closes.
	/// </summary>
	IEventSubscription Subscribe(int capacity = 256);

	/// <summary>
	/// Pull events from the ring buffer with id strictly greater than
	/// <paramref name="lastEventId"/>. Used by the SSE controller to replay missed
	/// events on reconnect (<c>Last-Event-ID</c> header).
	/// Returns empty when no replay is available (lastEventId is null, unknown, or already current).
	/// </summary>
	IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId);
}

/// <summary>A single live subscription. Disposing it removes the subscriber from fan-out.</summary>
public interface IEventSubscription :IAsyncDisposable {
	ChannelReader<ServerEvent> Reader { get; }
}
