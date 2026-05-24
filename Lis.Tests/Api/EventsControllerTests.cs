using System.Text;

using Lis.Api.Controllers;
using Lis.Core.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Tests <see cref="EventsController"/>. We wire a real <see cref="EventBus"/> to a real
/// controller, point it at a <see cref="DefaultHttpContext"/> whose response body is a
/// <see cref="MemoryStream"/>, and assert on the SSE frames written out.
/// </summary>
public class EventsControllerTests {

	[Fact]
	public async Task Stream_Sets_SSE_Headers_On_Connect() {
		EventBus bus = new();
		EventsController controller = new(bus);
		(HttpContext ctx, MemoryStream body) = MakeHttpContext();
		controller.ControllerContext = new ControllerContext { HttpContext = ctx };

		using CancellationTokenSource cts = new();
		Task streamTask = controller.Stream(lastEventId: null, cts.Token);

		// Give the controller a tick to set headers / subscribe.
		await Task.Delay(50);

		Assert.Equal("text/event-stream", ctx.Response.Headers.ContentType);
		Assert.Equal("no-cache",          ctx.Response.Headers.CacheControl);
		Assert.Equal("no",                ctx.Response.Headers["X-Accel-Buffering"]);

		await cts.CancelAsync();
		await SafeAwait(streamTask);
		_ = body; // assertions live in the headers for this test
	}

	[Fact]
	public async Task Stream_Forwards_Live_Events_As_SSE_Frames() {
		EventBus bus = new();
		EventsController controller = new(bus);
		(HttpContext ctx, MemoryStream body) = MakeHttpContext();
		controller.ControllerContext = new ControllerContext { HttpContext = ctx };

		using CancellationTokenSource cts = new();
		Task streamTask = controller.Stream(lastEventId: null, cts.Token);

		// Give the controller time to subscribe before we publish.
		await Task.Delay(50);

		await bus.PublishAsync(new ServerEvent(0, "project_created", new { id = "abc" }, DateTimeOffset.UtcNow));
		await bus.PublishAsync(new ServerEvent(0, "project_updated", new { id = "abc" }, DateTimeOffset.UtcNow));

		// Allow the writer to flush.
		await Task.Delay(50);

		await cts.CancelAsync();
		await SafeAwait(streamTask);

		string output = Encoding.UTF8.GetString(body.ToArray());
		Assert.Contains("id: 1\nevent: project_created\ndata: {\"id\":\"abc\"}\n\n", output);
		Assert.Contains("id: 2\nevent: project_updated\ndata: {\"id\":\"abc\"}\n\n", output);
	}

	[Fact]
	public async Task Stream_Replays_Buffered_Events_When_LastEventId_Provided() {
		EventBus bus = new();
		// Publish before any subscriber arrives.
		await bus.PublishAsync(new ServerEvent(0, "project_created", new { id = "a" }, DateTimeOffset.UtcNow));
		await bus.PublishAsync(new ServerEvent(0, "project_updated", new { id = "a" }, DateTimeOffset.UtcNow));
		await bus.PublishAsync(new ServerEvent(0, "project_deleted", new { id = "a" }, DateTimeOffset.UtcNow));

		EventsController controller = new(bus);
		(HttpContext ctx, MemoryStream body) = MakeHttpContext();
		controller.ControllerContext = new ControllerContext { HttpContext = ctx };

		using CancellationTokenSource cts = new();
		// Client missed everything after id 1 → expect ids 2 and 3 to replay.
		Task streamTask = controller.Stream(lastEventId: "1", cts.Token);
		await Task.Delay(50);
		await cts.CancelAsync();
		await SafeAwait(streamTask);

		string output = Encoding.UTF8.GetString(body.ToArray());
		Assert.DoesNotContain("id: 1\n", output);
		Assert.Contains("id: 2\nevent: project_updated", output);
		Assert.Contains("id: 3\nevent: project_deleted", output);
	}

	[Fact]
	public async Task Stream_Without_LastEventId_Skips_Replay_And_Only_Tails_Live() {
		EventBus bus = new();
		await bus.PublishAsync(new ServerEvent(0, "stale", new { }, DateTimeOffset.UtcNow));

		EventsController controller = new(bus);
		(HttpContext ctx, MemoryStream body) = MakeHttpContext();
		controller.ControllerContext = new ControllerContext { HttpContext = ctx };

		using CancellationTokenSource cts = new();
		Task streamTask = controller.Stream(lastEventId: null, cts.Token);
		await Task.Delay(50);

		await bus.PublishAsync(new ServerEvent(0, "fresh", new { }, DateTimeOffset.UtcNow));
		await Task.Delay(50);

		await cts.CancelAsync();
		await SafeAwait(streamTask);

		string output = Encoding.UTF8.GetString(body.ToArray());
		Assert.DoesNotContain("event: stale", output);
		Assert.Contains("event: fresh",       output);
	}

	[Fact]
	public async Task Stream_Serializes_Payloads_With_Snake_Case() {
		EventBus bus = new();
		EventsController controller = new(bus);
		(HttpContext ctx, MemoryStream body) = MakeHttpContext();
		controller.ControllerContext = new ControllerContext { HttpContext = ctx };

		using CancellationTokenSource cts = new();
		Task streamTask = controller.Stream(lastEventId: null, cts.Token);
		await Task.Delay(50);

		await bus.PublishAsync(new ServerEvent(
			0,
			"x",
			new ProjectPayloadFixture(ProjectId: "abc", IsArchived: true),
			DateTimeOffset.UtcNow
		));
		await Task.Delay(50);

		await cts.CancelAsync();
		await SafeAwait(streamTask);

		string output = Encoding.UTF8.GetString(body.ToArray());
		Assert.Contains("\"project_id\":\"abc\"", output);
		Assert.Contains("\"is_archived\":true",    output);
	}

	private sealed record ProjectPayloadFixture(string ProjectId, bool IsArchived);

	private static (HttpContext ctx, MemoryStream body) MakeHttpContext() {
		DefaultHttpContext ctx = new();
		MemoryStream body = new();
		ctx.Response.Body = body;
		return (ctx, body);
	}

	private static async Task SafeAwait(Task t) {
		try { await t; } catch (OperationCanceledException) { /* expected */ }
	}
}
