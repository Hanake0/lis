using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

/// <summary>
/// Server → client WebSocket frame. Flat shape; <see cref="Type"/> is the discriminator.
/// Null fields are omitted on serialization (see JsonOpt).
/// </summary>
public sealed class ServerFrame {
	[JsonPropertyName("type")]
	public required string Type { get; init; }

	[JsonPropertyName("chat_id")]
	public string? ChatId { get; init; }

	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	[JsonPropertyName("role")]
	public string? Role { get; init; }

	[JsonPropertyName("timestamp")]
	public DateTimeOffset? Timestamp { get; init; }

	[JsonPropertyName("emoji")]
	public string? Emoji { get; init; }

	[JsonPropertyName("error")]
	public string? ErrorMessage { get; init; }

	[JsonPropertyName("chats")]
	public IReadOnlyList<ChatSummary>? Chats { get; init; }

	[JsonPropertyName("messages")]
	public IReadOnlyList<MessageRow>? Messages { get; init; }

	// Streaming fields
	[JsonPropertyName("delta")]
	public string? Delta { get; init; }

	[JsonPropertyName("done")]
	public bool? Done { get; init; }

	[JsonPropertyName("tool_name")]
	public string? ToolName { get; init; }

	[JsonPropertyName("tool_args")]
	public string? ToolArgs { get; init; }

	[JsonPropertyName("result")]
	public string? Result { get; init; }

	// Agent info
	[JsonPropertyName("agent_name")]
	public string? AgentName { get; init; }

	[JsonPropertyName("display_name")]
	public string? DisplayName { get; init; }

	[JsonPropertyName("model")]
	public string? Model { get; init; }

	[JsonPropertyName("effort")]
	public string? Effort { get; init; }

	[JsonPropertyName("tool_profile")]
	public string? ToolProfileField { get; init; }

	[JsonPropertyName("agents")]
	public IReadOnlyList<AgentSummary>? Agents { get; init; }

	// Token usage (on message frames)
	[JsonPropertyName("input_tokens")]
	public int? InputTokens { get; init; }

	[JsonPropertyName("output_tokens")]
	public int? OutputTokens { get; init; }

	[JsonPropertyName("cache_read_tokens")]
	public int? CacheReadTokens { get; init; }

	[JsonPropertyName("cache_creation_tokens")]
	public int? CacheCreationTokens { get; init; }

	[JsonPropertyName("thinking_tokens")]
	public int? ThinkingTokens { get; init; }

	// Context breakdown (response to client's get_context)
	[JsonPropertyName("total")]
	public int? Total { get; init; }

	[JsonPropertyName("budget")]
	public int? Budget { get; init; }

	[JsonPropertyName("segments")]
	public IReadOnlyList<ContextSegment>? Segments { get; init; }

	public static ServerFrame Connected() =>
		new() { Type = "connected" };

	public static ServerFrame Message(string chatId, string id, string text, string role, DateTimeOffset timestamp,
		int? inputTokens = null, int? outputTokens = null, int? cacheReadTokens = null,
		int? cacheCreationTokens = null, int? thinkingTokens = null) =>
		new() {
			Type = "message", ChatId = chatId, Id = id, Text = text, Role = role, Timestamp = timestamp,
			InputTokens = inputTokens, OutputTokens = outputTokens,
			CacheReadTokens = cacheReadTokens, CacheCreationTokens = cacheCreationTokens,
			ThinkingTokens = thinkingTokens
		};

	/// <summary>
	/// Late-arriving token counts for an existing message (typically a tool result, whose
	/// cost is only known after the next assistant turn returns usage with an input delta).
	/// The id is the original frame id the client used to identify the bubble/block.
	/// </summary>
	public static ServerFrame MessageTokens(string chatId, string id,
		int? inputTokens = null, int? outputTokens = null, int? cacheReadTokens = null,
		int? cacheCreationTokens = null, int? thinkingTokens = null) =>
		new() {
			Type = "message_tokens", ChatId = chatId, Id = id,
			InputTokens = inputTokens, OutputTokens = outputTokens,
			CacheReadTokens = cacheReadTokens, CacheCreationTokens = cacheCreationTokens,
			ThinkingTokens = thinkingTokens
		};

	public static ServerFrame Typing(string chatId) =>
		new() { Type = "typing", ChatId = chatId };

	public static ServerFrame TypingStopped(string chatId) =>
		new() { Type = "typing_stopped", ChatId = chatId };

	public static ServerFrame Reaction(string chatId, string messageId, string emoji) =>
		new() { Type = "reaction", ChatId = chatId, Id = messageId, Emoji = emoji };

	public static ServerFrame Error(string error, string? chatId = null) =>
		new() { Type = "error", ErrorMessage = error, ChatId = chatId };

	public static ServerFrame ChatList(IReadOnlyList<ChatSummary> chats) =>
		new() { Type = "chat_list", Chats = chats };

	public static ServerFrame ChatHistory(string chatId, IReadOnlyList<MessageRow> messages) =>
		new() { Type = "chat_history", ChatId = chatId, Messages = messages };

	public static ServerFrame Context(string chatId, int total, int budget, IReadOnlyList<ContextSegment> segments) =>
		new() { Type = "context", ChatId = chatId, Total = total, Budget = budget, Segments = segments };

	// Streaming factories
	public static ServerFrame Chunk(string chatId, string messageId, string delta, bool done) =>
		new() { Type = "chunk", ChatId = chatId, Id = messageId, Delta = delta, Done = done };

	public static ServerFrame ThinkingChunk(string chatId, string messageId, string delta, bool done) =>
		new() { Type = "thinking_chunk", ChatId = chatId, Id = messageId, Delta = delta, Done = done };

	public static ServerFrame ToolStart(string chatId, string messageId, string toolName, string args) =>
		new() { Type = "tool_start", ChatId = chatId, Id = messageId, ToolName = toolName, ToolArgs = args };

	public static ServerFrame ToolResult(string chatId, string messageId, string toolName, string result, string? error = null) =>
		new() { Type = "tool_result", ChatId = chatId, Id = messageId, ToolName = toolName, Result = result, ErrorMessage = error };

	// Agent management factories
	public static ServerFrame AgentInfo(string chatId, string agentName, string? displayName, string model, string? effort, string? toolProfile) =>
		new() { Type = "agent_info", ChatId = chatId, AgentName = agentName, DisplayName = displayName, Model = model, Effort = effort, ToolProfileField = toolProfile };

	public static ServerFrame AgentList(IReadOnlyList<AgentSummary> agents) =>
		new() { Type = "agent_list", Agents = agents };
}
