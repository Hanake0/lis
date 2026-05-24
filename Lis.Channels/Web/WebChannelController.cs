using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Lis.Channels.Web.Schemas;
using Lis.Core.Channel;
using Lis.Core.Util;
using Lis.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lis.Channels.Web;

[ApiController]
[Route("web")]
[Tags("WebChannel")]
public sealed class WebChannelController(
	WebChannelClient                client,
	IConversationService            conversationService,
	IOptions<WebChannelOptions>     options,
	IServiceScopeFactory            scopeFactory,
	ILogger<WebChannelController>   logger) : ControllerBase {

	[HttpGet("ws")]
	public async Task HandleWebSocket([FromQuery] string? token) {
		if (!this.HttpContext.WebSockets.IsWebSocketRequest) {
			this.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		if (string.IsNullOrEmpty(options.Value.AuthToken) || token != options.Value.AuthToken) {
			this.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		using WebSocket socket = await this.HttpContext.WebSockets.AcceptWebSocketAsync();
		Guid connId = client.Register(socket);
		CancellationToken ct = this.HttpContext.RequestAborted;

		try {
			await client.SendDirectAsync(connId, ServerFrame.Connected(), ct);
			await this.ReadLoopAsync(socket, ct);
		} catch (OperationCanceledException) {
			// Client disconnected — normal exit
		} catch (WebSocketException ex) {
			if (logger.IsEnabled(LogLevel.Debug))
				logger.LogDebug(ex, "Web socket closed unexpectedly");
		} catch (Exception ex) {
			logger.LogError(ex, "Web socket handler crashed");
		} finally {
			client.Unregister(connId);
			if (socket.State == WebSocketState.Open) {
				try {
					await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
				} catch { /* best effort */ }
			}
		}
	}

	// ── Read loop ───────────────────────────────────────────────────

	private async Task ReadLoopAsync(WebSocket socket, CancellationToken ct) {
		byte[] buffer = new byte[8192];
		StringBuilder pending = new();

		while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open) {
			WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

			if (result.MessageType == WebSocketMessageType.Close) {
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", ct);
				return;
			}

			if (result.MessageType != WebSocketMessageType.Text) continue;

			pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
			if (!result.EndOfMessage) continue;

			string json = pending.ToString();
			pending.Clear();

			ClientFrame? frame;
			try {
				frame = JsonSerializer.Deserialize<ClientFrame>(json, JsonOpt.Default);
			} catch (JsonException ex) {
				logger.LogWarning(ex, "Malformed client frame: {Json}", json);
				continue;
			}

			if (frame is null) continue;
			await this.DispatchClientFrameAsync(frame, ct);
		}
	}

	// ── Frame dispatch ──────────────────────────────────────────────

	private async Task DispatchClientFrameAsync(ClientFrame frame, CancellationToken ct) {
		switch (frame.Type) {
			case "message":     await this.HandleMessageFrameAsync(frame, ct);  break;
			case "history":     await this.HandleHistoryFrameAsync(frame, ct);  break;
			case "list_chats":  await this.HandleListChatsFrameAsync(ct);       break;
			default:
				await client.PublishAsync(ServerFrame.Error($"unknown frame type: {frame.Type}"), ct);
				break;
		}
	}

	private async Task HandleMessageFrameAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId) || string.IsNullOrEmpty(frame.Text)) {
			await client.PublishAsync(ServerFrame.Error("message frame requires chat_id and text", frame.ChatId), ct);
			return;
		}

		MessageDispatchMode dispatch = ParseDispatch(frame.Dispatch);

		IncomingMessage incoming = new() {
			ExternalId = $"web:{Guid.NewGuid():N}",
			ChatId     = frame.ChatId,
			SenderId   = options.Value.OwnerId,
			SenderName = options.Value.OwnerName,
			Timestamp  = DateTimeOffset.UtcNow,
			Body       = frame.Text,
			Dispatch   = dispatch,
		};

		// Echo the user's own message back so other connected clients (and the sender on reload) see it.
		await client.PublishAsync(
			ServerFrame.Message(frame.ChatId, incoming.ExternalId, frame.Text, role: "user", timestamp: incoming.Timestamp),
			ct);

		// Fire-and-forget — same pattern as the WhatsApp webhook controller.
		_ = Task.Run(async () => {
			try {
				await conversationService.HandleIncomingAsync(incoming, CancellationToken.None);
			} catch (Exception ex) {
				logger.LogError(ex, "Error processing web message {Id} in {ChatId}", incoming.ExternalId, frame.ChatId);
				await client.PublishAsync(ServerFrame.Error(ex.Message, frame.ChatId), CancellationToken.None);
			}
		}, ct);
	}

	private async Task HandleHistoryFrameAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("history frame requires chat_id"), ct);
			return;
		}

		int limit = Math.Clamp(frame.Limit ?? 100, 1, 500);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<MessageRow> rows = await db.Messages
			.Where(m => m.Chat.ExternalId == frame.ChatId && !m.Queued)
			.OrderByDescending(m => m.Id)
			.Take(limit)
			.OrderBy(m => m.Id)
			.Select(m => new MessageRow {
				Id        = m.ExternalId ?? m.Id.ToString(),
				Role      = m.Role ?? (m.IsFromMe ? "assistant" : "user"),
				Text      = m.Body,
				Timestamp = m.Timestamp,
			})
			.ToListAsync(ct);

		await client.PublishAsync(ServerFrame.ChatHistory(frame.ChatId, rows), ct);
	}

	private async Task HandleListChatsFrameAsync(CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<ChatSummary> chats = await db.Chats
			.OrderByDescending(c => c.UpdatedAt)
			.Select(c => new ChatSummary {
				ChatId    = c.ExternalId,
				Name      = c.Name,
				UpdatedAt = c.UpdatedAt,
			})
			.ToListAsync(ct);

		await client.PublishAsync(ServerFrame.ChatList(chats), ct);
	}

	private static MessageDispatchMode ParseDispatch(string? value) => value switch {
		"default"    => MessageDispatchMode.Default,
		"queue_only" => MessageDispatchMode.QueueOnly,
		"interrupt"  => MessageDispatchMode.Interrupt,
		_            => MessageDispatchMode.Immediate,
	};
}
