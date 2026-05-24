using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.MultiRun;
using Lis.Api.Services;
using Lis.Core.Events;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for the MultiRun fan-out — controller + coordinator + dispatcher together.
/// The coordinator returns the run id synchronously; targets dispatch on a background
/// task, so the tests wait for the expected number of progress events to land before
/// asserting.
/// </summary>
public sealed class MultiRunTests :IDisposable {
	private readonly ServiceProvider     services;
	private readonly FakeTimeProvider    time;
	private readonly CapturingEventBus   bus;
	private readonly RecordingDispatcher dispatcher;

	public MultiRunTests() {
		this.time       = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus        = new CapturingEventBus();
		this.dispatcher = new RecordingDispatcher();

		ServiceCollection sc = new();
		sc.AddSingleton<TimeProvider>(this.time);
		sc.AddSingleton<IEventBus>(this.bus);
		sc.AddScoped<IMultiRunDispatcher>(_ => this.dispatcher);
		sc.AddSingleton<IMultiRunCoordinator, MultiRunCoordinator>();
		this.services = sc.BuildServiceProvider();
	}

	public void Dispose() {
		this.services.Dispose();
		GC.SuppressFinalize(this);
	}

	[Fact]
	public void Controller_Run_Returns_202_Accepted_With_RunId() {
		MultiRunController sut = new(this.services.GetRequiredService<IMultiRunCoordinator>());

		ActionResult<MultiRunResponse> result = sut.Run(new MultiRunRequest(
			Prompt:  "do it",
			Targets: [new MultiRunTargetDto("chat-1", null, null)]
		));

		AcceptedResult accepted = Assert.IsType<AcceptedResult>(result.Result);
		MultiRunResponse body = Assert.IsType<MultiRunResponse>(accepted.Value);
		Assert.False(string.IsNullOrWhiteSpace(body.RunId));
	}

	[Fact]
	public async Task Coordinator_Dispatches_All_Targets_And_Emits_Progress_Per_Target() {
		IMultiRunCoordinator coordinator = this.services.GetRequiredService<IMultiRunCoordinator>();

		string runId = coordinator.Start("do it", [
			new MultiRunTarget("chat-1", null, null),
			new MultiRunTarget("chat-2", "agent-x", "model-y"),
		]);

		await this.bus.WaitForCountAsync(2, TimeSpan.FromSeconds(2));

		Assert.Equal(2, this.dispatcher.Calls.Count);
		Assert.All(this.bus.Captured, ev => Assert.Equal(MultiRunEventNames.Progress, ev.Name));
		Assert.All(this.bus.Captured, ev => Assert.Equal(runId, ((MultiRunProgressPayload)ev.Data).RunId));
	}

	[Fact]
	public async Task Coordinator_Reports_Error_When_Dispatcher_Throws() {
		this.dispatcher.ThrowOnNext = true;
		IMultiRunCoordinator coordinator = this.services.GetRequiredService<IMultiRunCoordinator>();

		coordinator.Start("do it", [new MultiRunTarget("chat-1", null, null)]);

		await this.bus.WaitForCountAsync(1, TimeSpan.FromSeconds(2));

		ServerEvent ev = Assert.Single(this.bus.Captured);
		MultiRunProgressPayload payload = Assert.IsType<MultiRunProgressPayload>(ev.Data);
		Assert.Equal("error", payload.Status);
		Assert.Equal("boom",  payload.Error);
	}

	[Fact]
	public void Coordinator_Empty_Targets_Throws_ArgumentException() {
		IMultiRunCoordinator coordinator = this.services.GetRequiredService<IMultiRunCoordinator>();
		Assert.Throws<ArgumentException>(() => coordinator.Start("do it", []));
	}

	[Fact]
	public void Coordinator_Empty_Prompt_Throws_ArgumentException() {
		IMultiRunCoordinator coordinator = this.services.GetRequiredService<IMultiRunCoordinator>();
		Assert.Throws<ArgumentException>(() => coordinator.Start("   ", [new MultiRunTarget("chat-1", null, null)]));
	}

	// ── Helpers ────────────────────────────────────────────────────

	private sealed class RecordingDispatcher :IMultiRunDispatcher {
		public List<MultiRunTarget> Calls       { get; } = [];
		public bool                 ThrowOnNext { get; set; }

		public Task<(string status, string? error)> DispatchAsync(MultiRunTarget target, string prompt, CancellationToken ct) {
			lock (this.Calls) this.Calls.Add(target);
			if (this.ThrowOnNext) {
				this.ThrowOnNext = false;
				throw new InvalidOperationException("boom");
			}
			return Task.FromResult<(string, string?)>(("ok", null));
		}
	}

	private sealed class CapturingEventBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			lock (this.Captured) this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];

		public async Task WaitForCountAsync(int expected, TimeSpan timeout) {
			DateTime deadline = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < deadline) {
				lock (this.Captured) if (this.Captured.Count >= expected) return;
				await Task.Delay(25);
			}
		}
	}
}
