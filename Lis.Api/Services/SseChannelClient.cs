using Lis.Api.Schemas.Events;
using Lis.Core.Channel;
using Lis.Core.Events;
using Lis.Core.Util;

namespace Lis.Api.Services;

/// <summary>
/// <see cref="IChannelClient"/> implementation that publishes every event to
/// <see cref="IEventBus"/> with <c>Subject = "chat:{chatId}"</c>. The
/// <c>ChatStreamController</c> subscribes with a matching filter so each
/// chat's UI only sees its own stream — no broadcast bandwidth on every
/// connected sidebar.
///
/// All sends are awaited so the agent loop's per-chunk ordering survives the
/// channel-fanout. The bus's bounded subscriber queues use DropOldest under
/// pressure, so a slow client only loses old chunks (never blocks the loop).
/// </summary>
public sealed class SseChannelClient(IEventBus events) :IChannelClient {

	private static string SubjectFor(string chatId) => $"chat:{chatId}";

	// ── Persisted-message echoes ───────────────────────────────────

	[Trace("SseChannelClient > SendMessageAsync")]
	public Task<string?> SendMessageAsync(
		string chatId, string message, string? replyToId = null, CancellationToken ct = default
	) => this.SendMessageAsync(chatId, message, usage: null, replyToId, ct);

	[Trace("SseChannelClient > SendMessageAsync (with usage)")]
	public async Task<string?> SendMessageAsync(
		string chatId, string message, TokenUsage? usage, string? replyToId = null, CancellationToken ct = default
	) {
		string id = Guid.NewGuid().ToString("N");
		ChatMessagePayload payload = new(
			ChatId:              chatId,
			Id:                  id,
			Text:                message,
			Role:                "assistant",
			Timestamp:           DateTimeOffset.UtcNow,
			InputTokens:         usage?.InputTokens,
			OutputTokens:        usage?.OutputTokens,
			CacheReadTokens:     usage?.CacheReadTokens,
			CacheCreationTokens: usage?.CacheCreationTokens,
			ThinkingTokens:      usage?.ThinkingTokens
		);
		await this.PublishAsync(ChatStreamEventNames.Message, payload, SubjectFor(chatId), ct);
		return id;
	}

	// ── Markers ────────────────────────────────────────────────────

	[Trace("SseChannelClient > SetTypingAsync")]
	public Task SetTypingAsync(string chatId, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.Typing, new TypingPayload(chatId), SubjectFor(chatId), ct);

	[Trace("SseChannelClient > StopTypingAsync")]
	public Task StopTypingAsync(string chatId, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.TypingStopped, new TypingStoppedPayload(chatId), SubjectFor(chatId), ct);

	public Task MarkReadAsync(string messageId, string chatId, CancellationToken ct = default) => Task.CompletedTask;

	[Trace("SseChannelClient > ReactAsync")]
	public Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.Reaction, new ReactionPayload(chatId, messageId, emoji), SubjectFor(chatId), ct);

	public Task<MediaDownload?> DownloadMediaAsync(
		string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default
	) => Task.FromResult<MediaDownload?>(null);

	// ── Streaming ──────────────────────────────────────────────────

	public Task SendChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.Chunk, new ChunkPayload(chatId, messageId, delta, done), SubjectFor(chatId), ct);

	public Task SendThinkingChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.ThinkingChunk, new ThinkingChunkPayload(chatId, messageId, delta, done), SubjectFor(chatId), ct);

	public Task SendToolStartAsync(string chatId, string messageId, string toolName, string args, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.ToolStart, new ToolStartPayload(chatId, messageId, toolName, args), SubjectFor(chatId), ct);

	public Task SendToolResultAsync(string chatId, string messageId, string toolName, string result, string? error, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.ToolResult, new ToolResultPayload(chatId, messageId, toolName, result, error), SubjectFor(chatId), ct);

	public Task SendErrorAsync(string chatId, string error, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.Error, new ChatErrorPayload(chatId, error), SubjectFor(chatId), ct);

	public Task SendMessageTokensAsync(string chatId, string id, TokenUsage usage, CancellationToken ct = default)
		=> this.PublishAsync(ChatStreamEventNames.MessageTokens, new MessageTokensPayload(
			ChatId:              chatId,
			Id:                  id,
			InputTokens:         usage.InputTokens,
			OutputTokens:        usage.OutputTokens,
			CacheReadTokens:     usage.CacheReadTokens,
			CacheCreationTokens: usage.CacheCreationTokens,
			ThinkingTokens:      usage.ThinkingTokens
		), SubjectFor(chatId), ct);

	// ── Interactive prompts ────────────────────────────────────────

	public async Task<bool> SendQuestionAsync(
		string chatId, string questionId, IReadOnlyList<QuestionItem> questions, CancellationToken ct = default
	) {
		QuestionItemPayload[] items = questions.Select(q => new QuestionItemPayload(
			Question: q.Question,
			Header:   q.Header,
			Multiple: q.Multiple,
			Options:  (q.Options ?? []).Select(o => new QuestionOptionPayload(o.Label, o.Description)).ToArray()
		)).ToArray();

		await this.PublishAsync(ChatStreamEventNames.Question, new QuestionPayload(chatId, questionId, items), SubjectFor(chatId), ct);
		return true;
	}

	public async Task<bool> SendPermissionRequestAsync(
		string chatId, string approvalId, string toolName,
		string? command = null, string? cwd = null, string? description = null, CancellationToken ct = default
	) {
		Dictionary<string, object?> metadata = new();
		if (command is not null)     metadata["command"]     = command;
		if (cwd is not null)         metadata["cwd"]         = cwd;
		if (description is not null) metadata["description"] = description;

		PermissionRequestPayload payload = new(
			ChatId:     chatId,
			ApprovalId: approvalId,
			ToolName:   toolName,
			Patterns:   command is not null ? new[] { command } : null,
			Metadata:   metadata.Count > 0 ? metadata : null,
			Always:     command is not null ? new[] { command } : null
		);
		await this.PublishAsync(ChatStreamEventNames.PermissionRequest, payload, SubjectFor(chatId), ct);
		return true;
	}

	// ── Internals ──────────────────────────────────────────────────

	// The bus returns ValueTask but IChannelClient methods expose Task — await
	// inside so the caller gets a real Task and CA2012 stays happy (ValueTask
	// can only be consumed once, can't be returned/stored).
	private async Task PublishAsync(string name, object payload, string subject, CancellationToken ct) =>
		await events.PublishAsync(new ServerEvent(0, name, payload, DateTimeOffset.UtcNow, subject), ct);
}
