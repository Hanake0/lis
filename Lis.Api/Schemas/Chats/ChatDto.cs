using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Chats;

/// <summary>
/// Lightweight chat representation returned by the REST API. The full
/// historical-summary shape used by the sidebar is not yet ported (Stage 13
/// will replace WebChannel-list-chats with /api/chats). Today this DTO
/// supports the create endpoint required by the multi-run orchestrator.
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
