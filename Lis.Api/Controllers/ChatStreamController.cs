using System.Text.Json;

using Lis.Core.Events;
using Lis.Core.Util;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Per-chat server-sent events stream — one long-lived GET per chat the UI is
/// actively rendering. Subscribes to <see cref="IEventBus"/> with a filter
/// matching <c>Subject == "chat:{chatId}"</c> so the subscriber's bounded queue
/// is never pressured by unrelated chat traffic. Unlike <c>EventsController</c>
/// the stream is intentionally NOT replayable — chat events are tied to a turn's
/// life (chunks/typing/etc) and lose meaning after the turn ends; clients
/// re-subscribe per turn rather than tailing across restarts.
/// </summary>
[ApiController]
[Route("api/chats")]
[Tags("ChatStream")]
public sealed class ChatStreamController(IEventBus events) :ControllerBase {

	[HttpGet("{chatExternalId}/stream")]
	[Produces("text/event-stream")]
	public async Task Stream(
		[FromRoute] string chatExternalId,
		CancellationToken  ct
	) {
		this.Response.Headers.ContentType  = "text/event-stream";
		this.Response.Headers.CacheControl = "no-cache";
		// Disable nginx buffering — SSE needs immediate flush per event.
		this.Response.Headers["X-Accel-Buffering"] = "no";

		// Flush headers immediately so EventSource transitions to OPEN — see EventsController
		// for the rationale. Without this the per-chat stream sits in CONNECTING until the
		// first turn event publishes.
		await this.Response.StartAsync(ct);
		await this.Response.WriteAsync(": connected\n\n", ct);
		await this.Response.Body.FlushAsync(ct);

		string subject = $"chat:{chatExternalId}";
		await using IEventSubscription sub = events.Subscribe(filter: ev => ev.Subject == subject);
		try {
			await foreach (ServerEvent ev in sub.Reader.ReadAllAsync(ct))
				await WriteEventAsync(this.Response, ev, ct);
		} catch (OperationCanceledException) {
			// Client disconnected — normal SSE termination.
		}
	}

	private static async Task WriteEventAsync(HttpResponse response, ServerEvent ev, CancellationToken ct) {
		string json = JsonSerializer.Serialize(ev.Data, JsonOpt.Default);
		await response.WriteAsync($"id: {ev.Id}\nevent: {ev.Name}\ndata: {json}\n\n", ct);
		await response.Body.FlushAsync(ct);
	}
}
