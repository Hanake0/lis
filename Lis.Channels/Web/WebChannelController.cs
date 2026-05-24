using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Lis.Agent;
using Lis.Channels.Web.Schemas;
using Lis.Core.Channel;
using Lis.Core.Util;
using Lis.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lis.Channels.Web;

[ApiController]
[Route("web")]
[Tags("WebChannel")]
public sealed class WebChannelController(
	WebChannelClient                client,
	IConversationService            conversationService,
	ContextBreakdownService         contextBreakdown,
	IApprovalService                approvalService,
	IOptions<WebChannelOptions>     options,
	IServiceScopeFactory            scopeFactory,
	IHostApplicationLifetime        lifetime,
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
		// Link client-disconnect + app-shutdown so a CTRL+C doesn't get stuck waiting
		// for the next inbound frame from an idle browser session.
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
			this.HttpContext.RequestAborted, lifetime.ApplicationStopping);
		CancellationToken ct = linkedCts.Token;

		try {
			await client.SendDirectAsync(connId, ServerFrame.Connected(), ct);
			await this.ReadLoopAsync(socket, ct);
		} catch (OperationCanceledException) {
			// Client disconnected or host stopping — normal exit
		} catch (WebSocketException ex) {
			if (logger.IsEnabled(LogLevel.Debug))
				logger.LogDebug(ex, "Web socket closed unexpectedly");
		} catch (Exception ex) {
			logger.LogError(ex, "Web socket handler crashed");
		} finally {
			client.Unregister(connId);
			if (socket.State == WebSocketState.Open) {
				try {
					using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(2));
					await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", closeCts.Token);
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
			case "message":             await this.HandleMessageFrameAsync(frame, ct);         break;
			case "history":             await this.HandleHistoryFrameAsync(frame, ct);         break;
			case "list_chats":          await this.HandleListChatsFrameAsync(ct);              break;
			case "create_session":      await this.HandleCreateSessionAsync(frame, ct);        break;
			case "delete_session":      await this.HandleDeleteSessionAsync(frame, ct);        break;
			case "rename_session":      await this.HandleRenameSessionAsync(frame, ct);        break;
			case "archive_session":     await this.HandleArchiveSessionAsync(frame, ct);       break;
			case "set_agent":           await this.HandleSetAgentAsync(frame, ct);             break;
			case "get_agent":           await this.HandleGetAgentAsync(frame, ct);             break;
			case "list_agents":         await this.HandleListAgentsAsync(ct);                  break;
			case "save_overrides":      await this.HandleSaveOverridesAsync(frame, ct);        break;
			case "get_context":         await this.HandleGetContextAsync(frame, ct);           break;
			case "abort":               await this.HandleAbortAsync(frame);                    break;
			case "permission_response": await this.HandlePermissionResponseAsync(frame, ct);   break;
			default:
				await client.PublishAsync(ServerFrame.Error($"unknown frame type: {frame.Type}"), ct);
				break;
		}
	}

	private async Task HandleMessageFrameAsync(ClientFrame frame, CancellationToken ct) {
		bool hasText        = !string.IsNullOrEmpty(frame.Text);
		bool hasAttachments = frame.Attachments is { Count: > 0 };

		if (string.IsNullOrEmpty(frame.ChatId) || (!hasText && !hasAttachments)) {
			await client.PublishAsync(ServerFrame.Error("message frame requires chat_id and text or attachments", frame.ChatId), ct);
			return;
		}

		MessageDispatchMode dispatch = ParseDispatch(frame.Dispatch);

		// Parse inline image attachments from base64 data URLs
		List<(byte[] Data, string MimeType)>? images = null;
		if (hasAttachments) {
			images = new();
			foreach (ClientImageAttachment att in frame.Attachments!) {
				if (string.IsNullOrEmpty(att.DataUrl) || string.IsNullOrEmpty(att.MimeType)) continue;
				if (!att.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;

				byte[]? data = ParseDataUrl(att.DataUrl);
				if (data is null || data.Length == 0) continue;
				images.Add((data, att.MimeType));
			}
		}

		(byte[]? firstImageData, string? firstImageMime) = images is { Count: > 0 }
			? (images[0].Data, images[0].MimeType)
			: (null, null);

		IncomingMessage incoming = new() {
			ExternalId           = $"web:{Guid.NewGuid():N}",
			ChatId               = frame.ChatId,
			SenderId             = options.Value.OwnerId,
			SenderName           = options.Value.OwnerName,
			Timestamp            = DateTimeOffset.UtcNow,
			Body                 = frame.Text,
			Channel              = "web",
			Dispatch             = dispatch,
			ModelOverride        = frame.Model,
			EffortOverride       = frame.Effort,
			ToolProfileOverride  = frame.ToolProfile,
			ThinkingModeOverride = frame.ThinkingMode,
			MediaType            = firstImageData is not null ? "image" : null,
			InlineMediaData      = firstImageData,
			InlineMediaMimeType  = firstImageMime,
		};

		string echoText = frame.Text ?? (firstImageData is not null ? "[image]" : "");
		await client.PublishAsync(
			ServerFrame.Message(frame.ChatId, incoming.ExternalId, echoText, role: "user", timestamp: incoming.Timestamp),
			ct);

		_ = Task.Run(async () => {
			try {
				await conversationService.HandleIncomingAsync(incoming, CancellationToken.None);
			} catch (Exception ex) {
				logger.LogError(ex, "Error processing web message {Id} in {ChatId}", incoming.ExternalId, frame.ChatId);
				await client.PublishAsync(ServerFrame.Error(ex.Message, frame.ChatId), CancellationToken.None);
			}
		}, ct);
	}

	private static byte[]? ParseDataUrl(string dataUrl) {
		int commaIdx = dataUrl.IndexOf(',');
		if (commaIdx < 0) return null;

		string base64 = dataUrl[(commaIdx + 1)..];
		try {
			return Convert.FromBase64String(base64);
		} catch (FormatException) {
			return null;
		}
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
			ToolName            = m.Role == "tool" ? ExtractToolName(m.SkContent)   : null,
			ToolArgs            = m.Role == "tool" ? ExtractToolArgs(m.SkContent)   : null,
			ToolIsError         = m.Role == "tool" ? ExtractToolIsError(m.SkContent) : null,
			ThinkingContent     = m.Role == "assistant" ? ExtractThinking(m.SkContent) : null,
		}).ToList();

		await client.PublishAsync(ServerFrame.ChatHistory(frame.ChatId, rows), ct);
	}

	private async Task HandleListChatsFrameAsync(CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		List<ChatSummary> chats = await db.Chats
			.Where(c => c.Channel == "web")
			.OrderByDescending(c => c.UpdatedAt)
			.Select(c => new ChatSummary {
				ChatId     = c.ExternalId,
				Name       = c.Name,
				UpdatedAt  = c.UpdatedAt,
				ArchivedAt = c.ArchivedAt,
			})
			.ToListAsync(ct);

		await client.PublishAsync(ServerFrame.ChatList(chats), ct);
	}

	// ── Session CRUD ────────────────────────────────────────────────

	private async Task HandleCreateSessionAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("create_session requires chat_id"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? existing = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		if (existing is not null) {
			await client.PublishAsync(ServerFrame.ChatCreated(existing.ExternalId, existing.Name, existing.UpdatedAt), ct);
			return;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		Persistence.Entities.ChatEntity chat = new() {
			ExternalId = frame.ChatId,
			Name       = frame.Name ?? "New Chat",
			Channel    = "web",
			CreatedAt  = now,
			UpdatedAt  = now,
			Enabled    = true,
		};
		db.Chats.Add(chat);
		await db.SaveChangesAsync(ct);

		await client.PublishAsync(ServerFrame.ChatCreated(chat.ExternalId, chat.Name, chat.UpdatedAt), ct);
	}

	private async Task HandleDeleteSessionAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("delete_session requires chat_id"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		if (chat is null) {
			await client.PublishAsync(ServerFrame.ChatDeleted(frame.ChatId), ct);
			return;
		}

		db.Chats.Remove(chat);
		await db.SaveChangesAsync(ct);

		await client.PublishAsync(ServerFrame.ChatDeleted(frame.ChatId), ct);
	}

	private async Task HandleRenameSessionAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId) || string.IsNullOrEmpty(frame.Name)) {
			await client.PublishAsync(ServerFrame.Error("rename_session requires chat_id and name"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		if (chat is null) {
			await client.PublishAsync(ServerFrame.Error("chat not found", frame.ChatId), ct);
			return;
		}

		chat.Name      = frame.Name;
		chat.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(ct);

		await client.PublishAsync(ServerFrame.ChatRenamed(frame.ChatId, chat.Name), ct);
	}

	private async Task HandleArchiveSessionAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("archive_session requires chat_id"), ct);
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		Persistence.Entities.ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == frame.ChatId, ct);

		if (chat is null) {
			await client.PublishAsync(ServerFrame.Error("chat not found", frame.ChatId), ct);
			return;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		chat.ArchivedAt = now;
		chat.UpdatedAt  = now;
		await db.SaveChangesAsync(ct);

		await client.PublishAsync(ServerFrame.ChatArchived(frame.ChatId, now), ct);
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

	// ── Abort ───────────────────────────────────────────────────────

	private async Task HandleAbortAsync(ClientFrame frame) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("abort frame requires chat_id"), CancellationToken.None);
			return;
		}

		if (conversationService is MessageDebouncer debouncer)
			await debouncer.AbortAsync(frame.ChatId);
	}

	// ── Permission response ────────────────────────────────────────

	private async Task HandlePermissionResponseAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ApprovalId) || string.IsNullOrEmpty(frame.Decision)) {
			await client.PublishAsync(ServerFrame.Error("permission_response requires approval_id and decision", frame.ChatId), ct);
			return;
		}

		ApprovalDecision decision = frame.Decision switch {
			"once"   => ApprovalDecision.Once,
			"always" => ApprovalDecision.Always,
			"deny"   => ApprovalDecision.Deny,
			_        => ApprovalDecision.Deny,
		};

		bool resolved = await approvalService.ResolveAsync(frame.ApprovalId, decision, "web");

		if (!resolved)
			await client.PublishAsync(ServerFrame.Error($"No pending approval with ID '{frame.ApprovalId}'", frame.ChatId), ct);
	}

	// ── Context breakdown ───────────────────────────────────────────

	private async Task HandleGetContextAsync(ClientFrame frame, CancellationToken ct) {
		if (string.IsNullOrEmpty(frame.ChatId)) {
			await client.PublishAsync(ServerFrame.Error("get_context requires chat_id"), ct);
			return;
		}

		ContextBreakdownService.Breakdown? breakdown;
		try {
			breakdown = await contextBreakdown.ComputeAsync(frame.ChatId, ct, frame.ForceRefresh == true);
		} catch (Exception ex) {
			logger.LogError(ex, "Failed to compute context breakdown for {ChatId}", frame.ChatId);
			await client.PublishAsync(ServerFrame.Error($"context breakdown failed: {ex.Message}", frame.ChatId), ct);
			return;
		}

		if (breakdown is null) {
			await client.PublishAsync(ServerFrame.Error("chat has no active session yet", frame.ChatId), ct);
			return;
		}

		List<ContextSegment> segments = breakdown.Segments
			.Select(s => new ContextSegment { Name = s.Name, Tokens = s.Tokens })
			.ToList();

		await client.PublishAsync(
			ServerFrame.Context(frame.ChatId, breakdown.Total, breakdown.Budget, segments,
				breakdown.Model, breakdown.Effort), ct);
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

	/// <summary>
	/// Reads <c>Metadata.ToolArgs</c> from a persisted tool message's SkContent — populated
	/// by ToolRunner so history can show the args the agent passed to the function.
	/// </summary>
	private static string? ExtractToolArgs(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("ToolArgs", out JsonElement args))
				return args.GetString();
		} catch { /* malformed JSON — return null */ }
		return null;
	}

	/// <summary>
	/// Reads <c>Metadata.LisThinking</c> from a persisted assistant message's SkContent —
	/// populated by ToolRunner with the accumulated thinking text as it was streamed, so
	/// the placeholder block on history reload shows exactly what the user saw live.
	/// </summary>
	private static string? ExtractThinking(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("LisThinking", out JsonElement think)
			 && think.ValueKind == JsonValueKind.String)
				return think.GetString();
		} catch { /* malformed JSON — return null */ }
		return null;
	}

	/// <summary>
	/// Reads <c>Metadata.IsError</c> from a persisted tool message's SkContent — populated
	/// by ToolRunner so history can render failed calls in their original failed state.
	/// </summary>
	private static bool? ExtractToolIsError(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("IsError", out JsonElement flag)
			 && (flag.ValueKind == JsonValueKind.True || flag.ValueKind == JsonValueKind.False))
				return flag.GetBoolean();
		} catch { /* malformed JSON — return null */ }
		return null;
	}
}
