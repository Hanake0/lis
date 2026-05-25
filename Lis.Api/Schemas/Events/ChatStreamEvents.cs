using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/chats/{id}/stream</c> by the SSE publisher.</summary>
public static class ChatStreamEventNames {
	public const string Message            = "message";
	public const string Chunk              = "chunk";
	public const string ThinkingChunk      = "thinking_chunk";
	public const string ToolStart          = "tool_start";
	public const string ToolResult         = "tool_result";
	public const string Typing             = "typing";
	public const string TypingStopped      = "typing_stopped";
	public const string Reaction           = "reaction";
	public const string MessageTokens      = "message_tokens";
	public const string PermissionRequest  = "permission_request";
	public const string Question           = "question";
	public const string Error              = "error";
}

/// <summary>
/// Persisted message echo — fires for both user-side echoes (after the client
/// POSTs a message) and assistant-side replies (after the agent loop completes
/// a turn). The id matches the message's external_id so history reload + live
/// updates can dedup.
/// </summary>
public sealed record ChatMessagePayload(
	[property: JsonPropertyName("chat_id")]                string         ChatId,
	[property: JsonPropertyName("id")]                     string         Id,
	[property: JsonPropertyName("text")]                   string?        Text,
	[property: JsonPropertyName("role")]                   string         Role,
	[property: JsonPropertyName("timestamp")]              DateTimeOffset Timestamp,
	[property: JsonPropertyName("input_tokens")]           int?           InputTokens,
	[property: JsonPropertyName("output_tokens")]          int?           OutputTokens,
	[property: JsonPropertyName("cache_read_tokens")]      int?           CacheReadTokens,
	[property: JsonPropertyName("cache_creation_tokens")]  int?           CacheCreationTokens,
	[property: JsonPropertyName("thinking_tokens")]        int?           ThinkingTokens
);

/// <summary>Incremental token chunk for a streaming assistant message.</summary>
public sealed record ChunkPayload(
	[property: JsonPropertyName("chat_id")]    string ChatId,
	[property: JsonPropertyName("message_id")] string MessageId,
	[property: JsonPropertyName("delta")]      string Delta,
	[property: JsonPropertyName("done")]       bool   Done
);

/// <summary>Incremental thinking chunk (separate event so the UI can render in a different block).</summary>
public sealed record ThinkingChunkPayload(
	[property: JsonPropertyName("chat_id")]    string ChatId,
	[property: JsonPropertyName("message_id")] string MessageId,
	[property: JsonPropertyName("delta")]      string Delta,
	[property: JsonPropertyName("done")]       bool   Done
);

/// <summary>Tool invocation start — payload is enough to render the "calling X with args" block.</summary>
public sealed record ToolStartPayload(
	[property: JsonPropertyName("chat_id")]    string ChatId,
	[property: JsonPropertyName("message_id")] string MessageId,
	[property: JsonPropertyName("tool_name")]  string ToolName,
	[property: JsonPropertyName("args")]       string Args
);

/// <summary>Tool invocation result (or error, if <c>error</c> is non-null).</summary>
public sealed record ToolResultPayload(
	[property: JsonPropertyName("chat_id")]    string  ChatId,
	[property: JsonPropertyName("message_id")] string  MessageId,
	[property: JsonPropertyName("tool_name")]  string  ToolName,
	[property: JsonPropertyName("result")]     string  Result,
	[property: JsonPropertyName("error")]      string? Error
);

/// <summary>Marker — the agent loop is generating; the UI shows a typing indicator.</summary>
public sealed record TypingPayload([property: JsonPropertyName("chat_id")] string ChatId);

/// <summary>Marker — the agent loop finished generating or aborted.</summary>
public sealed record TypingStoppedPayload([property: JsonPropertyName("chat_id")] string ChatId);

/// <summary>Reaction to a message (emoji on a specific message id).</summary>
public sealed record ReactionPayload(
	[property: JsonPropertyName("chat_id")]    string ChatId,
	[property: JsonPropertyName("message_id")] string MessageId,
	[property: JsonPropertyName("emoji")]      string Emoji
);

/// <summary>
/// Post-turn token-usage update. Sent after the message body has streamed in
/// chunks — lets the UI replace the in-flight estimate with the API's actual count.
/// </summary>
public sealed record MessageTokensPayload(
	[property: JsonPropertyName("chat_id")]                string ChatId,
	[property: JsonPropertyName("id")]                     string Id,
	[property: JsonPropertyName("input_tokens")]           int?   InputTokens,
	[property: JsonPropertyName("output_tokens")]          int?   OutputTokens,
	[property: JsonPropertyName("cache_read_tokens")]      int?   CacheReadTokens,
	[property: JsonPropertyName("cache_creation_tokens")]  int?   CacheCreationTokens,
	[property: JsonPropertyName("thinking_tokens")]        int?   ThinkingTokens
);

/// <summary>
/// A tool wants approval before running. The client renders a prompt and
/// POSTs the user's decision to <c>/api/permissions/{approval_id}/respond</c>.
/// </summary>
public sealed record PermissionRequestPayload(
	[property: JsonPropertyName("chat_id")]      string                       ChatId,
	[property: JsonPropertyName("approval_id")]  string                       ApprovalId,
	[property: JsonPropertyName("tool_name")]    string                       ToolName,
	[property: JsonPropertyName("patterns")]     IReadOnlyList<string>?       Patterns,
	[property: JsonPropertyName("metadata")]     IReadOnlyDictionary<string, object?>? Metadata,
	[property: JsonPropertyName("always")]       IReadOnlyList<string>?       Always
);

/// <summary>
/// A tool wants the user to pick from a set of options. The client renders the
/// form and POSTs answers to <c>/api/questions/{question_id}/respond</c>.
/// </summary>
public sealed record QuestionPayload(
	[property: JsonPropertyName("chat_id")]      string                       ChatId,
	[property: JsonPropertyName("question_id")]  string                       QuestionId,
	[property: JsonPropertyName("questions")]    IReadOnlyList<QuestionItemPayload> Questions
);

public sealed record QuestionItemPayload(
	[property: JsonPropertyName("question")]    string                            Question,
	[property: JsonPropertyName("header")]      string?                           Header,
	[property: JsonPropertyName("multiple")]    bool                              Multiple,
	[property: JsonPropertyName("options")]     IReadOnlyList<QuestionOptionPayload> Options
);

public sealed record QuestionOptionPayload(
	[property: JsonPropertyName("label")]       string  Label,
	[property: JsonPropertyName("description")] string? Description
);

/// <summary>Error surfaced from the agent loop or tool runner.</summary>
public sealed record ChatErrorPayload(
	[property: JsonPropertyName("chat_id")] string ChatId,
	[property: JsonPropertyName("error")]   string Error
);
