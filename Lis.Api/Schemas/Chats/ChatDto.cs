using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Chats;

/// <summary>
/// Chat-summary representation returned by <c>GET /api/chats</c> (sidebar
/// bootstrap) and <c>POST /api/chats</c> (multi-run + new-chat flows). The
/// rich per-message history travels on a separate endpoint — this DTO carries
/// only the fields the sidebar's session-list rendering needs.
/// </summary>
public sealed record ChatDto(
	[property: JsonPropertyName("chat_id")]      string  ChatId,
	[property: JsonPropertyName("name")]         string? Name,
	[property: JsonPropertyName("project_id")]   string? ProjectExternalId,
	[property: JsonPropertyName("pinned_at")]    DateTimeOffset? PinnedAt,
	[property: JsonPropertyName("archived_at")]  DateTimeOffset? ArchivedAt,
	[property: JsonPropertyName("created_at")]   DateTimeOffset CreatedAt,
	[property: JsonPropertyName("updated_at")]   DateTimeOffset UpdatedAt
) {
	public static ChatDto From(ChatEntity c) => new(
		ChatId:            c.ExternalId,
		Name:              c.Name,
		ProjectExternalId: c.Project?.ExternalId,
		PinnedAt:          c.PinnedAt,
		ArchivedAt:        c.ArchivedAt,
		CreatedAt:         c.CreatedAt,
		UpdatedAt:         c.UpdatedAt
	);
}

/// <summary>
/// Create-chat request. <c>chat_id</c> is optional — when absent the server mints
/// one (<c>web:{guid}</c> shape). <c>project_id</c> binds the chat under a project
/// by its external_id (404 if missing).
/// </summary>
public sealed record CreateChatRequest(
	[property: JsonPropertyName("chat_id")] [MaxLength(64)]                 string? ChatId,
	[property: JsonPropertyName("name")] [MaxLength(256)]                   string? Name,
	[property: JsonPropertyName("project_id")] [MaxLength(64)]              string? ProjectExternalId,
	[property: JsonPropertyName("channel")] [MaxLength(32)]                 string? Channel
);

/// <summary>
/// Patch a chat. Today only <c>name</c> is patchable (rename) — broader edits
/// (project move, agent reassignment, override save) go through their own
/// dedicated endpoints. Null fields are left unchanged.
/// </summary>
public sealed record UpdateChatRequest(
	[property: JsonPropertyName("name")] [MaxLength(256)] string? Name
);

/// <summary>
/// One-shot LLM invocation against a chat's agent/model. Drives the git view's
/// commit-message and PR-description generation — the front-end sends a visible
/// prompt + an optional hidden block carrying structured-output instructions,
/// and parses JSON out of the assistant text we return. The exchange is NOT
/// saved to chat history.
/// </summary>
public sealed record GenerateRequest(
	[property: JsonPropertyName("visible_prompt")] [Required]   string  VisiblePrompt,
	[property: JsonPropertyName("hidden_prompt")]              string? HiddenPrompt,
	[property: JsonPropertyName("kind")] [MaxLength(32)] string? Kind,
	[property: JsonPropertyName("model")] [MaxLength(128)] string? Model
);

public sealed record GenerateResponseDto(
	[property: JsonPropertyName("text")]          string Text,
	[property: JsonPropertyName("input_tokens")]  int    InputTokens,
	[property: JsonPropertyName("output_tokens")] int    OutputTokens
);

/// <summary>
/// Reassign a chat's bound agent. The agent must exist (404 otherwise) and the
/// chat must exist (404 otherwise). Effective agent for new messages becomes
/// this one immediately.
/// </summary>
public sealed record SetChatAgentRequest(
	[property: JsonPropertyName("agent_name")] [Required, MaxLength(64)] string AgentName
);

/// <summary>
/// Save runtime overrides onto the chat's bound agent (or the system default
/// when the chat has no explicit agent — same fallback the legacy WS frame
/// applied). Null fields are left unchanged; empty strings are not accepted
/// (use the agents-CRUD endpoint to clear an optional field to null).
/// </summary>
public sealed record SaveChatOverridesRequest(
	[property: JsonPropertyName("model")] [MaxLength(128)]        string? Model,
	[property: JsonPropertyName("effort")] [MaxLength(16)]        string? Effort,
	[property: JsonPropertyName("tool_profile")] [MaxLength(32)]  string? ToolProfile
);

/// <summary>
/// One image attachment delivered inline as a base64 data URL — mirrors the WS
/// frame format so the webview's compose logic doesn't need re-encoding.
/// </summary>
public sealed record SendMessageAttachment(
	[property: JsonPropertyName("data_url")] [Required]  string DataUrl,
	[property: JsonPropertyName("mime_type")] [Required] string MimeType
);

/// <summary>
/// User-side message dispatch. Either <c>text</c> OR at least one attachment is
/// required. Per-turn overrides (model/effort/tool_profile/thinking_mode) apply
/// only to this message — the agent's defaults aren't mutated. <c>dispatch</c>
/// is "immediate" / "default" / "queue_only" / "interrupt" — anything else falls
/// back to "immediate" (matches the legacy WS frame's permissive parsing).
/// </summary>
public sealed record SendMessageRequest(
	[property: JsonPropertyName("text")]          string?                       Text,
	[property: JsonPropertyName("dispatch")]      string?                       Dispatch,
	[property: JsonPropertyName("model")]         string?                       Model,
	[property: JsonPropertyName("effort")]        string?                       Effort,
	[property: JsonPropertyName("tool_profile")]  string?                       ToolProfile,
	[property: JsonPropertyName("thinking_mode")] string?                       ThinkingMode,
	[property: JsonPropertyName("attachments")]   List<SendMessageAttachment>?  Attachments
);

/// <summary>
/// Response from <c>POST /api/chats/{id}/messages</c>. The chat's agent loop
/// runs asynchronously; the client opens <c>GET /api/chats/{id}/stream</c> in
/// parallel to consume the token stream. The <c>message_id</c> is the user-side
/// echo's id — useful for client-side ack tracking.
/// </summary>
public sealed record SendMessageResponse(
	[property: JsonPropertyName("message_id")] string MessageId
);

/// <summary>
/// One labelled slice of the context-window breakdown — system prompt, summary,
/// per-turn messages, etc. <c>tokens = -1</c> means the counter failed for that
/// slice; the UI renders "—" rather than zeroing it (we never silently
/// substitute a local tokenizer for an Anthropic count).
/// </summary>
public sealed record ContextSegmentDto(
	[property: JsonPropertyName("name")]   string Name,
	[property: JsonPropertyName("tokens")] int    Tokens
);

/// <summary>
/// Context-window panel data. <c>total</c> is the authoritative API-reported
/// input-side count (input + cache-read + cache-create from the last assistant
/// turn). <c>budget</c> is the configured model budget. <c>segments</c> sum to
/// roughly the total (segments use subtraction / token-count calls for the few
/// pieces not derivable from the persisted totals).
/// </summary>
public sealed record ContextBreakdownDto(
	[property: JsonPropertyName("chat_id")]  string                Id,
	[property: JsonPropertyName("total")]    int                   Total,
	[property: JsonPropertyName("budget")]   int                   Budget,
	[property: JsonPropertyName("model")]    string                Model,
	[property: JsonPropertyName("effort")]   string?               Effort,
	[property: JsonPropertyName("segments")] ContextSegmentDto[]   Segments
);
