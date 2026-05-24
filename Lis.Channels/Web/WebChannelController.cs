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
			case "message":        await this.HandleMessageFrameAsync(frame, ct);    break;
			case "history":        await this.HandleHistoryFrameAsync(frame, ct);    break;
			case "list_chats":     await this.HandleListChatsFrameAsync(ct);         break;
			case "set_agent":      await this.HandleSetAgentAsync(frame, ct);        break;
			case "get_agent":      await this.HandleGetAgentAsync(frame, ct);        break;
			case "list_agents":    await this.HandleListAgentsAsync(ct);             break;
			case "save_overrides": await this.HandleSaveOverridesAsync(frame, ct);   break;
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
			ExternalId          = $"web:{Guid.NewGuid():N}",
			ChatId              = frame.ChatId,
			SenderId            = options.Value.OwnerId,
			SenderName          = options.Value.OwnerName,
			Timestamp           = DateTimeOffset.UtcNow,
			Body                = frame.Text,
			Dispatch            = dispatch,
			ModelOverride       = frame.Model,
			EffortOverride      = frame.Effort,
			ToolProfileOverride = frame.ToolProfile,
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

		var rawRows = await db.Messages
			.Where(m => m.Chat.ExternalId == frame.ChatId && !m.Queued)
			.OrderByDescending(m => m.Id)
			.Take(limit)
			.OrderBy(m => m.Id)
			.Select(m => new {
				Id                  = m.ExternalId ?? m.Id.ToString(),
				Role                = m.Role ?? (m.IsFromMe ? "assistant" : "user"),
				Text                = m.Body,
				m.Timestamp,
				m.InputTokens,
				m.OutputTokens,
				m.CacheReadTokens,
				m.CacheCreationTokens,
				m.ThinkingTokens,
				m.SkContent,
			})
			.ToListAsync(ct);

		List<MessageRow> rows = rawRows.Select(m => new MessageRow {
			Id                  = m.Id,
			Role                = m.Role,
			Text                = m.Text,
			Timestamp           = m.Timestamp,
			InputTokens         = m.InputTokens,
			OutputTokens        = m.OutputTokens,
			CacheReadTokens     = m.CacheReadTokens,
			CacheCreationTokens = m.CacheCreationTokens,
			ThinkingTokens      = m.ThinkingTokens,
			ToolName            = m.Role == "tool" ? ExtractToolName(m.SkContent) : null,
		}).ToList();

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

	// ── Agent management ────────────────────────────────────────────

	private async Task HandleSetAgentAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId) || string.IsNullOrEmpty(frame.AgentName)) {
			await client.PublishAsync(ServerFrame.Error("set_agent requires chat_id and agent_name", frame.ChatId), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.AgentEntity? agent = await db.Agents
			.FirstOrDefaultAsync(a => a.Name == frame.AgentName, ct);
		if (agent is null) {
			await client.PublishAsync(ServerFrame.Error($"agent '{frame.AgentName}' not found", frame.ChatId), ct);
			return;
		}

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);
		if (chat is not null) {
			chat.AgentId = agent.Id;
			await db.SaveChangesAsync(ct);
		}

		await client.PublishAsync(
			ServerFrame.AgentInfo(frame.ChatId, agent.Name, agent.DisplayName, agent.Model, agent.ThinkingEffort, agent.ToolProfile), ct);
	}

	private async Task HandleGetAgentAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("get_agent requires chat_id"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		Persistence.Entities.AgentEntity? agent = chat?.Agent
			?? await db.Agents.FirstOrDefaultAsync(a => a.IsDefault, ct);

		if (agent is null) {
			await client.PublishAsync(ServerFrame.Error("no agent found", frame.ChatId), ct);
			return;
		}

		await client.PublishAsync(
			ServerFrame.AgentInfo(frame.ChatId, agent.Name, agent.DisplayName, agent.Model, agent.ThinkingEffort, agent.ToolProfile), ct);
	}

	private async Task HandleListAgentsAsync(CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<AgentSummary> agents = await db.Agents
			.OrderBy(a => a.Id)
			.Select(a => new AgentSummary {
				Name        = a.Name,
				DisplayName = a.DisplayName,
				Model       = a.Model,
				Effort      = a.ThinkingEffort,
				ToolProfile = a.ToolProfile,
				IsDefault   = a.IsDefault,
			})
			.ToListAsync(ct);

		await client.PublishAsync(ServerFrame.AgentList(agents), ct);
	}

	private async Task HandleSaveOverridesAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("save_overrides requires chat_id"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		Persistence.Entities.AgentEntity? agent = chat?.Agent
			?? await db.Agents.FirstOrDefaultAsync(a => a.IsDefault, ct);

		if (agent is null) {
			await client.PublishAsync(ServerFrame.Error("no agent found", frame.ChatId), ct);
			return;
		}

		if (frame.Model is { Length: > 0 })      agent.Model          = frame.Model;
		if (frame.Effort is { Length: > 0 })      agent.ThinkingEffort = frame.Effort;
		if (frame.ToolProfile is { Length: > 0 }) agent.ToolProfile    = frame.ToolProfile;
		agent.UpdatedAt = DateTimeOffset.UtcNow;

		await db.SaveChangesAsync(ct);

		await client.PublishAsync(
			ServerFrame.AgentInfo(frame.ChatId, agent.Name, agent.DisplayName, agent.Model, agent.ThinkingEffort, agent.ToolProfile), ct);
	}

	private static MessageDispatchMode ParseDispatch(string? value) => value switch {
		"default"    => MessageDispatchMode.Default,
		"queue_only" => MessageDispatchMode.QueueOnly,
		"interrupt"  => MessageDispatchMode.Interrupt,
		_            => MessageDispatchMode.Immediate,
	};

	private static string? ExtractToolName(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			// FunctionResultContent serializes with FunctionName
			if (doc.RootElement.TryGetProperty("FunctionName", out JsonElement fn))
				return fn.GetString();
			// Fallback: look in Items array
			if (doc.RootElement.TryGetProperty("Items", out JsonElement items) && items.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement item in items.EnumerateArray()) {
					if (item.TryGetProperty("FunctionName", out JsonElement itemFn))
						return itemFn.GetString();
				}
			}
		} catch { /* malformed JSON — return null */ }
		return null;
	}
}
