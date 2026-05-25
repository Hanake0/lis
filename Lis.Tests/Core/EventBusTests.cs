using Lis.Core.Events;

namespace Lis.Tests.Core;

public class EventBusTests {
	private static ServerEvent MakeEvent(string name, object data) =>
		new(0, name, data, DateTimeOffset.UtcNow);

	[Fact]
	public async Task Subscribers_Receive_Published_Events() {
		EventBus bus = new();
		await using IEventSubscription sub = bus.Subscribe();

		await bus.PublishAsync(MakeEvent("project_created", new { id = "p1" }));

		Assert.True(sub.Reader.TryRead(out ServerEvent? ev));
		Assert.NotNull(ev);
		Assert.Equal("project_created", ev!.Name);
		Assert.Equal(1, ev.Id);
	}

	[Fact]
	public async Task Each_Event_Gets_A_Monotonic_Id() {
		EventBus bus = new();
		await using IEventSubscription sub = bus.Subscribe();

		await bus.PublishAsync(MakeEvent("a", new { }));
		await bus.PublishAsync(MakeEvent("b", new { }));
		await bus.PublishAsync(MakeEvent("c", new { }));

		List<long> ids = [];
		while (sub.Reader.TryRead(out ServerEvent? ev)) ids.Add(ev!.Id);
		Assert.Equal([1L, 2L, 3L], ids);
	}

	[Fact]
	public async Task Multiple_Subscribers_All_Receive_Each_Event() {
		EventBus bus = new();
		await using IEventSubscription a = bus.Subscribe();
		await using IEventSubscription b = bus.Subscribe();
		await using IEventSubscription c = bus.Subscribe();

		await bus.PublishAsync(MakeEvent("ping", new { }));

		Assert.True(a.Reader.TryRead(out _));
		Assert.True(b.Reader.TryRead(out _));
		Assert.True(c.Reader.TryRead(out _));
	}

	[Fact]
	public async Task Disposed_Subscription_Stops_Receiving() {
		EventBus bus = new();
		IEventSubscription sub = bus.Subscribe();

		await bus.PublishAsync(MakeEvent("first", new { }));
		Assert.True(sub.Reader.TryRead(out _));

		await sub.DisposeAsync();
		await bus.PublishAsync(MakeEvent("second", new { }));

		// Reader should be completed; TryRead returns false and no item.
		Assert.False(sub.Reader.TryRead(out _));
	}

	[Fact]
	public async Task ReplaySince_Returns_Events_After_LastId() {
		EventBus bus = new();
		await using IEventSubscription _ = bus.Subscribe(); // drain channel-wise; not used here

		await bus.PublishAsync(MakeEvent("a", new { }));
		await bus.PublishAsync(MakeEvent("b", new { }));
		await bus.PublishAsync(MakeEvent("c", new { }));

		IReadOnlyList<ServerEvent> missed = bus.ReplaySince(1);
		Assert.Equal(2, missed.Count);
		Assert.Equal(2, missed[0].Id);
		Assert.Equal(3, missed[1].Id);
	}

	[Fact]
	public async Task ReplaySince_Null_Returns_Empty_But_Zero_Returns_All() {
		EventBus bus = new();
		await bus.PublishAsync(MakeEvent("a", new { }));
		await bus.PublishAsync(MakeEvent("b", new { }));

		// null means "client did not pass Last-Event-ID" → don't replay.
		Assert.Empty(bus.ReplaySince(null));

		// 0 (or negative) means "from the beginning" → return everything still in the ring.
		Assert.Equal(2, bus.ReplaySince(0).Count);
		Assert.Equal(2, bus.ReplaySince(-5).Count);
	}

	[Fact]
	public async Task ReplaySince_LastId_Beyond_Current_Returns_Empty() {
		EventBus bus = new();
		await bus.PublishAsync(MakeEvent("a", new { }));

		Assert.Empty(bus.ReplaySince(999));
	}

	[Fact]
	public async Task Ring_Buffer_Trims_By_Capacity() {
		EventBus bus = new(new EventBusOptions { RingCapacity = 3 });

		for (int i = 0; i < 10; i++)
			await bus.PublishAsync(MakeEvent($"ev{i}", new { i }));

		// Only the last 3 should be replayable.
		IReadOnlyList<ServerEvent> all = bus.ReplaySince(0);
		Assert.Equal(3, all.Count);
		Assert.Equal(8, all[0].Id);
		Assert.Equal(10, all[2].Id);
	}

	[Fact]
	public async Task Bounded_Subscriber_Channel_Drops_Oldest_Under_Backpressure() {
		EventBus bus = new();
		// Capacity 2; subscriber doesn't read.
		await using IEventSubscription sub = bus.Subscribe(capacity: 2);

		await bus.PublishAsync(MakeEvent("a", new { }));
		await bus.PublishAsync(MakeEvent("b", new { }));
		await bus.PublishAsync(MakeEvent("c", new { })); // drops "a"
		await bus.PublishAsync(MakeEvent("d", new { })); // drops "b"

		List<string> names = [];
		while (sub.Reader.TryRead(out ServerEvent? ev)) names.Add(ev!.Name);

		Assert.Equal(["c", "d"], names);
	}

	// ── Filtered subscriptions ────────────────────────────────────

	private static ServerEvent MakeChatEvent(string name, string chatId) =>
		new(0, name, new { }, DateTimeOffset.UtcNow, Subject: $"chat:{chatId}");

	[Fact]
	public async Task Filtered_Subscriber_Only_Receives_Events_Matching_The_Predicate() {
		EventBus bus = new();
		// Subscribes only to chat:c1's stream.
		await using IEventSubscription sub = bus.Subscribe(filter: ev => ev.Subject == "chat:c1");

		await bus.PublishAsync(MakeChatEvent("chunk", "c1"));
		await bus.PublishAsync(MakeChatEvent("chunk", "c2"));   // dropped by filter
		await bus.PublishAsync(MakeChatEvent("chunk", "c1"));
		await bus.PublishAsync(MakeEvent  ("global", new { })); // null Subject — dropped

		List<string?> seen = [];
		while (sub.Reader.TryRead(out ServerEvent? ev)) seen.Add(ev!.Subject);
		Assert.Equal(["chat:c1", "chat:c1"], seen);
	}

	[Fact]
	public async Task Unfiltered_Subscribers_Still_See_Everything_When_A_Filtered_One_Is_Present() {
		EventBus bus = new();
		await using IEventSubscription unfiltered = bus.Subscribe();
		await using IEventSubscription filtered   = bus.Subscribe(filter: ev => ev.Subject == "chat:c1");

		await bus.PublishAsync(MakeChatEvent("chunk", "c1"));
		await bus.PublishAsync(MakeChatEvent("chunk", "c2"));
		await bus.PublishAsync(MakeEvent  ("global", new { }));

		int unfilteredCount = 0;
		while (unfiltered.Reader.TryRead(out _)) unfilteredCount++;
		int filteredCount = 0;
		while (filtered.Reader.TryRead(out _)) filteredCount++;

		Assert.Equal(3, unfilteredCount);
		Assert.Equal(1, filteredCount);
	}

	[Fact]
	public async Task Filtered_Subscriber_Sees_Monotonic_Ids_Even_Though_Some_Events_Were_Dropped() {
		EventBus bus = new();
		await using IEventSubscription sub = bus.Subscribe(filter: ev => ev.Subject == "chat:c1");

		await bus.PublishAsync(MakeChatEvent("chunk", "c1"));   // id=1, kept
		await bus.PublishAsync(MakeChatEvent("chunk", "c2"));   // id=2, dropped
		await bus.PublishAsync(MakeChatEvent("chunk", "c1"));   // id=3, kept

		List<long> ids = [];
		while (sub.Reader.TryRead(out ServerEvent? ev)) ids.Add(ev!.Id);
		Assert.Equal([1L, 3L], ids);
	}

	[Fact]
	public async Task Late_Subscriber_Misses_Earlier_Events_But_Can_Replay() {
		EventBus bus = new();
		await bus.PublishAsync(MakeEvent("early", new { }));

		await using IEventSubscription sub = bus.Subscribe();
		await bus.PublishAsync(MakeEvent("late", new { }));

		// Channel only saw "late".
		Assert.True(sub.Reader.TryRead(out ServerEvent? ev));
		Assert.Equal("late", ev!.Name);

		// Ring buffer can replay both.
		Assert.Equal(2, bus.ReplaySince(0).Count);
	}
}
