using System.Collections.Concurrent;
using System.Threading.Channels;

using SysChannel = System.Threading.Channels.Channel;

namespace Lis.Core.Events;

/// <summary>
/// In-memory <see cref="IEventBus"/> implementation. Single-process only — fine for
/// the current Lis deployment topology (one API instance per user). When we eventually
/// scale horizontally, swap this for a Redis/NATS-backed implementation behind the same
/// interface.
/// </summary>
public sealed class EventBus(EventBusOptions? options = null) :IEventBus {
	private readonly EventBusOptions opts = options ?? new EventBusOptions();
	private long nextId; // 1-based; 0 is reserved for "no previous id".
	private readonly ConcurrentDictionary<Guid, Subscription> subscribers = new();
	private readonly LinkedList<ServerEvent> ring = new();
	private readonly Lock ringLock = new();

	public async ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
		// Assign the id here so callers don't need to think about ordering.
		long id = Interlocked.Increment(ref this.nextId);
		ServerEvent stamped = ev with { Id = id };

		// Append to ring + trim by both count and age.
		lock (this.ringLock) {
			this.ring.AddLast(stamped);
			while (this.ring.Count > this.opts.RingCapacity)
				this.ring.RemoveFirst();

			DateTimeOffset cutoff = DateTimeOffset.UtcNow - this.opts.RingMaxAge;
			while (this.ring.First is { Value.Timestamp: var ts } first && ts < cutoff)
				this.ring.Remove(first);
		}

		// Fan-out. WriteAsync blocks if a subscriber's channel is full —
		// bounded queues use DropOldest so this only blocks briefly under churn.
		foreach (Subscription sub in this.subscribers.Values) {
			if (sub.Filter is not null && !sub.Filter(stamped)) continue;
			try {
				await sub.Channel.Writer.WriteAsync(stamped, ct);
			} catch (ChannelClosedException) {
				// Subscription already disposed; let cleanup catch it.
			}
		}
	}

	public IEventSubscription Subscribe(int capacity = 256, Func<ServerEvent, bool>? filter = null) {
		Channel<ServerEvent> channel = SysChannel.CreateBounded<ServerEvent>(new BoundedChannelOptions(capacity) {
			FullMode     = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		});

		Guid id = Guid.NewGuid();
		Subscription sub = new(id, channel, this, filter);
		this.subscribers[id] = sub;
		return sub;
	}

	public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) {
		// `null` means "client did not pass Last-Event-ID" → no replay.
		// `0` or negative means "from the beginning" → return everything still in the ring.
		// Positive means "events strictly after this id".
		if (lastEventId is null) return [];

		long threshold = Math.Max(0, lastEventId.Value);
		lock (this.ringLock) {
			List<ServerEvent> missed = [];
			foreach (ServerEvent ev in this.ring)
				if (ev.Id > threshold) missed.Add(ev);
			return missed;
		}
	}

	internal void Unsubscribe(Guid id) {
		if (this.subscribers.TryRemove(id, out Subscription? sub))
			sub.Channel.Writer.TryComplete();
	}

	private sealed class Subscription(Guid id, Channel<ServerEvent> channel, EventBus owner, Func<ServerEvent, bool>? filter) :IEventSubscription {
		public Channel<ServerEvent> Channel { get; } = channel;
		public Func<ServerEvent, bool>? Filter { get; } = filter;
		public ChannelReader<ServerEvent> Reader => this.Channel.Reader;

		public ValueTask DisposeAsync() {
			owner.Unsubscribe(id);
			return ValueTask.CompletedTask;
		}
	}
}

public sealed record EventBusOptions {
	/// <summary>Maximum number of events held in the replay ring buffer.</summary>
	public int RingCapacity { get; init; } = 1024;

	/// <summary>Maximum age an event can have before it's evicted from the ring.</summary>
	public TimeSpan RingMaxAge { get; init; } = TimeSpan.FromMinutes(5);
}
