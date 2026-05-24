using System.Text.Json;

using Lis.Core.Events;
using Lis.Core.Util;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Global server-sent events stream. One long-lived GET per connected client.
/// Every mutation in the API publishes to <see cref="IEventBus"/>; this controller
/// fans those events out across the wire so sidebars stay in sync without polling.
///
/// On reconnect, clients pass the <c>Last-Event-ID</c> header; the bus replays
/// anything still in its ring buffer with a strictly greater id before transitioning
/// to live mode.
/// </summary>
[ApiController]
[Route("api/events")]
[Tags("Events")]
public sealed class EventsController(IEventBus events) :ControllerBase {

	[HttpGet]
	[Produces("text/event-stream")]
	public async Task Stream(
		[FromHeader(Name = "Last-Event-ID")] string? lastEventId,
		CancellationToken                            ct
	) {
		this.Response.Headers.ContentType  = "text/event-stream";
		this.Response.Headers.CacheControl = "no-cache";
		// Disable nginx buffering — SSE needs immediate flush per event.
		this.Response.Headers["X-Accel-Buffering"] = "no";

		// Replay anything the client missed across the reconnect.
		if (long.TryParse(lastEventId, out long since)) {
			foreach (ServerEvent ev in events.ReplaySince(since))
				await WriteEventAsync(this.Response, ev, ct);
		}

		// Live tail. The bounded subscriber channel uses DropOldest so a slow client
		// can never block the publisher; missed events fall back to ring-buffer replay.
		await using IEventSubscription sub = events.Subscribe();
		try {
			await foreach (ServerEvent ev in sub.Reader.ReadAllAsync(ct))
				await WriteEventAsync(this.Response, ev, ct);
		} catch (OperationCanceledException) {
			// Client disconnected — normal SSE termination.
		}
	}

	private static async Task WriteEventAsync(HttpResponse response, ServerEvent ev, CancellationToken ct) {
		string json = JsonSerializer.Serialize(ev.Data, JsonOpt.Default);
		// SSE frame: id, event, data terminated by a blank line.
		await response.WriteAsync($"id: {ev.Id}\nevent: {ev.Name}\ndata: {json}\n\n", ct);
		await response.Body.FlushAsync(ct);
	}
}
