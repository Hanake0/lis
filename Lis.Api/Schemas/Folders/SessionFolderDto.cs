using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Folders;

/// <summary>Wire representation of a <see cref="SessionFolderEntity"/>.</summary>
public sealed record SessionFolderDto(
	[property: JsonPropertyName("id")]                 string  Id,
	[property: JsonPropertyName("scope_key")]          string  ScopeKey,
	[property: JsonPropertyName("name")]               string  Name,
	[property: JsonPropertyName("parent_id")]          string? ParentId,
	[property: JsonPropertyName("sort_index")]         int     SortIndex,
	[property: JsonPropertyName("items")]              IReadOnlyList<SessionFolderItemDto> Items,
	[property: JsonPropertyName("created_at")]         DateTimeOffset CreatedAt,
	[property: JsonPropertyName("updated_at")]         DateTimeOffset UpdatedAt
) {
	public static SessionFolderDto From(SessionFolderEntity f) => new(
		Id:        f.ExternalId,
		ScopeKey:  f.ScopeKey,
		Name:      f.Name,
		ParentId:  f.ParentExternalId,
		SortIndex: f.SortIndex,
		Items:     f.Items.OrderBy(i => i.SortIndex).Select(SessionFolderItemDto.From).ToArray(),
		CreatedAt: f.CreatedAt,
		UpdatedAt: f.UpdatedAt
	);
}

public sealed record SessionFolderItemDto(
	[property: JsonPropertyName("chat_id")]    string ChatId,
	[property: JsonPropertyName("sort_index")] int    SortIndex,
	[property: JsonPropertyName("added_at")]   DateTimeOffset AddedAt
) {
	public static SessionFolderItemDto From(SessionFolderItemEntity i) =>
		new(ChatId: i.ChatExternalId, SortIndex: i.SortIndex, AddedAt: i.AddedAt);
}

public sealed record CreateSessionFolderRequest(
	[property: JsonPropertyName("scope_key")] [Required, MaxLength(512)] string  ScopeKey,
	[property: JsonPropertyName("name")] [Required, MaxLength(256)]      string  Name,
	[property: JsonPropertyName("parent_id")] [MaxLength(64)]            string? ParentId
);

public sealed record RenameSessionFolderRequest(
	[property: JsonPropertyName("name")] [Required, MaxLength(256)] string Name
);

public sealed record ReorderSessionFoldersRequest(
	[property: JsonPropertyName("scope_key")] [Required, MaxLength(512)]              string                ScopeKey,
	[property: JsonPropertyName("ordered_external_ids")] [Required]                   IReadOnlyList<string> OrderedExternalIds
);

public sealed record AddSessionsRequest(
	[property: JsonPropertyName("chat_ids")] [Required] IReadOnlyList<string> ChatIds
);
