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
	[property: JsonPropertyName("chat_id"), MaxLength(64)]                 string? ChatId,
	[property: JsonPropertyName("name"), MaxLength(256)]                   string? Name,
	[property: JsonPropertyName("project_id"), MaxLength(64)]              string? ProjectExternalId,
	[property: JsonPropertyName("channel"), MaxLength(32)]                 string? Channel
);

/// <summary>
/// Patch a chat. Today only <c>name</c> is patchable (rename) — broader edits
/// (project move, agent reassignment, override save) go through their own
/// dedicated endpoints. Null fields are left unchanged.
/// </summary>
public sealed record UpdateChatRequest(
	[property: JsonPropertyName("name"), MaxLength(256)] string? Name
);

/// <summary>
/// One-shot LLM invocation against a chat's agent/model. Drives the git view's
/// commit-message and PR-description generation — the front-end sends a visible
/// prompt + an optional hidden block carrying structured-output instructions,
/// and parses JSON out of the assistant text we return. The exchange is NOT
/// saved to chat history.
/// </summary>
public sealed record GenerateRequest(
	[property: JsonPropertyName("visible_prompt"), Required]   string  VisiblePrompt,
	[property: JsonPropertyName("hidden_prompt")]              string? HiddenPrompt,
	[property: JsonPropertyName("kind"),           MaxLength(32)] string? Kind,
	[property: JsonPropertyName("model"),          MaxLength(128)] string? Model
);

public sealed record GenerateResponseDto(
	[property: JsonPropertyName("text")]          string Text,
	[property: JsonPropertyName("input_tokens")]  int    InputTokens,
	[property: JsonPropertyName("output_tokens")] int    OutputTokens
);
