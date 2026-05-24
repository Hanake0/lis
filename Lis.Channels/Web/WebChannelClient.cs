using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

using Lis.Channels.Web.Schemas;
using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.Extensions.Logging;

namespace Lis.Channels.Web;

/// <summary>
/// Web channel publisher AND subscriber registry. Singleton.
///
/// Every connected WebSocket registers itself; every published frame is broadcast to
/// all connections. Clients filter by <c>chat_id</c> on the frame. The per-socket
/// <see cref="SemaphoreSlim"/> exists because <c>WebSocket.SendAsync</c> is not
/// concurrent-safe — multiple publishes to the same socket must serialize.
/// </summary>
public sealed class WebChannelClient(ILogger<WebChannelClient> logger) : IChannelClient {

	private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

	// ── Connection registry (called by WebChannelController) ────────

	public Guid Register(WebSocket socket) {
		Guid id = Guid.NewGuid();
		this._connections[id] = new Connection(socket);
		return id;
	}

	public void Unregister(Guid id) {
		if (this._connections.TryRemove(id, out Connection? conn))
			conn.Dispose();
	}

	// ── IChannelClient (called by ConversationService) ──────────────

	[Trace("WebChannelClient > SendMessageAsync")]
	public Task<string?> SendMessageAsync(
		string chatId, string message, string? replyToId = null, CancellationToken ct = default) =>
		this.SendMessageAsync(chatId, message, usage: null, replyToId, ct);

	[Trace("WebChannelClient > SendMessageAsync (with usage)")]
	public async Task<string?> SendMessageAsync(
		string chatId, string message, TokenUsage? usage, string? replyToId = null, CancellationToken ct = default) {
		string id = Guid.NewGuid().ToString("N");
		await this.PublishAsync(
			ServerFrame.Message(
				chatId, id, message, role: "assistant", timestamp: DateTimeOffset.UtcNow,
				inputTokens:         usage?.InputTokens,
				outputTokens:        usage?.OutputTokens,
				cacheReadTokens:     usage?.CacheReadTokens,
				cacheCreationTokens: usage?.CacheCreationTokens,
				thinkingTokens:      usage?.ThinkingTokens),
			ct);
		return id;
	}

	[Trace("WebChannelClient > SetTypingAsync")]
	public Task SetTypingAsync(string chatId, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.Typing(chatId), ct);

	[Trace("WebChannelClient > StopTypingAsync")]
	public Task StopTypingAsync(string chatId, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.TypingStopped(chatId), ct);

	public Task MarkReadAsync(string messageId, string chatId, CancellationToken ct = default) =>
		Task.CompletedTask;

	[Trace("WebChannelClient > ReactAsync")]
	public Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.Reaction(chatId, messageId, emoji), ct);

	public Task<MediaDownload?> DownloadMediaAsync(
		string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default) =>
		Task.FromResult<MediaDownload?>(null);

	// ── Streaming (IChannelClient defaults are no-op; web overrides) ─

	public Task SendChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.Chunk(chatId, messageId, delta, done), ct);

	public Task SendThinkingChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.ThinkingChunk(chatId, messageId, delta, done), ct);

	public Task SendToolStartAsync(string chatId, string messageId, string toolName, string args, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.ToolStart(chatId, messageId, toolName, args), ct);

	public Task SendToolResultAsync(string chatId, string messageId, string toolName, string result, string? error, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.ToolResult(chatId, messageId, toolName, result, error), ct);

	public Task SendErrorAsync(string chatId, string error, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.Error(error, chatId), ct);

	public Task SendMessageTokensAsync(string chatId, string id, TokenUsage usage, CancellationToken ct = default) =>
		this.PublishAsync(ServerFrame.MessageTokens(
			chatId, id,
			inputTokens:         usage.InputTokens,
			outputTokens:        usage.OutputTokens,
			cacheReadTokens:     usage.CacheReadTokens,
			cacheCreationTokens: usage.CacheCreationTokens,
			thinkingTokens:      usage.ThinkingTokens), ct);

	public async Task<bool> SendPermissionRequestAsync(string chatId, string approvalId, string toolName,
		string? command = null, string? cwd = null, string? description = null,
		CancellationToken ct = default) {
		Dictionary<string, object?> metadata = new();
		if (command is not null) metadata["command"] = command;
		if (cwd is not null) metadata["cwd"] = cwd;
		if (description is not null) metadata["description"] = description;

		await this.PublishAsync(
			ServerFrame.PermissionRequest(chatId, approvalId, toolName,
				patterns: command is not null ? [command] : null,
				metadata: metadata,
				always: command is not null ? [command] : null),
			ct);
		return true;
	}

	// ── Publishing ──────────────────────────────────────────────────

	public async Task PublishAsync(ServerFrame frame, CancellationToken ct = default) {
		if (this._connections.IsEmpty) return;

		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOpt.Default);
		List<Guid> dead = new();

		foreach ((Guid id, Connection conn) in this._connections) {
			if (conn.Socket.State != WebSocketState.Open) {
				dead.Add(id);
				continue;
			}

			try {
				await conn.SendLock.WaitAsync(ct);
				try {
					await conn.Socket.SendAsync(
						new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, ct);
				} finally {
					conn.SendLock.Release();
				}
			} catch (Exception ex) {
				if (logger.IsEnabled(LogLevel.Debug))
					logger.LogDebug(ex, "Failed to publish frame to web socket {ConnectionId}", id);
				dead.Add(id);
			}
		}

		foreach (Guid id in dead) this.Unregister(id);
	}

	/// <summary>
	/// Send a single frame to a specific connection. Used by the controller for the
	/// initial handshake before any broadcasts can target this socket.
	/// </summary>
	public async Task SendDirectAsync(Guid connectionId, ServerFrame frame, CancellationToken ct) {
		if (!this._connections.TryGetValue(connectionId, out Connection? conn)) return;

		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOpt.Default);
		await conn.SendLock.WaitAsync(ct);
		try {
			await conn.Socket.SendAsync(
				new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, ct);
		} finally {
			conn.SendLock.Release();
		}
	}

	private sealed class Connection(WebSocket socket) : IDisposable {
		public readonly WebSocket     Socket   = socket;
		public readonly SemaphoreSlim SendLock = new(1, 1);

		public void Dispose() => this.SendLock.Dispose();
	}
}
