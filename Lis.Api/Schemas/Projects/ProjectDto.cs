using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Projects;

/// <summary>Wire representation of a <see cref="ProjectEntity"/>. Snake-case JSON.</summary>
public sealed record ProjectDto(
	[property: JsonPropertyName("id")]                    string  Id,
	[property: JsonPropertyName("path")]                  string  Path,
	[property: JsonPropertyName("label")]                 string? Label,
	[property: JsonPropertyName("icon")]                  string? Icon,
	[property: JsonPropertyName("icon_image_mime")]       string? IconImageMime,
	[property: JsonPropertyName("icon_image_source")]     string? IconImageSource,
	[property: JsonPropertyName("icon_image_updated_at")] DateTimeOffset? IconImageUpdatedAt,
	[property: JsonPropertyName("icon_background")]       string? IconBackground,
	[property: JsonPropertyName("color")]                 string? Color,
	[property: JsonPropertyName("added_at")]              DateTimeOffset AddedAt,
	[property: JsonPropertyName("last_opened_at")]        DateTimeOffset? LastOpenedAt,
	[property: JsonPropertyName("created_at")]            DateTimeOffset CreatedAt,
	[property: JsonPropertyName("updated_at")]            DateTimeOffset UpdatedAt,
	[property: JsonPropertyName("sidebar_collapsed")]     bool SidebarCollapsed,
	[property: JsonPropertyName("sort_index")]            int SortIndex
) {
	public static ProjectDto From(ProjectEntity p) => new(
		Id:                 p.ExternalId,
		Path:               p.Path,
		Label:              p.Label,
		Icon:               p.Icon,
		IconImageMime:      p.IconImageMime,
		IconImageSource:    p.IconImageSource,
		IconImageUpdatedAt: p.IconImageUpdatedAt,
		IconBackground:     p.IconBackground,
		Color:              p.Color,
		AddedAt:            p.AddedAt,
		LastOpenedAt:       p.LastOpenedAt,
		CreatedAt:          p.CreatedAt,
		UpdatedAt:          p.UpdatedAt,
		SidebarCollapsed:   p.SidebarCollapsed,
		SortIndex:          p.SortIndex
	);
}

public sealed record CreateProjectRequest(
	[property: JsonPropertyName("path")] [Required, MaxLength(1024)] string  Path,
	[property: JsonPropertyName("label")] [MaxLength(256)]            string? Label,
	[property: JsonPropertyName("color")] [MaxLength(32)]             string? Color
);

public sealed record UpdateProjectRequest(
	[property: JsonPropertyName("label")] [MaxLength(256)]              string? Label,
	[property: JsonPropertyName("color")] [MaxLength(32)]               string? Color,
	[property: JsonPropertyName("icon")] [MaxLength(64)]                string? Icon,
	[property: JsonPropertyName("icon_background")] [MaxLength(16)]     string? IconBackground,
	[property: JsonPropertyName("sidebar_collapsed")]                  bool?   SidebarCollapsed
);

public sealed record ReorderProjectsRequest(
	[property: JsonPropertyName("ordered_external_ids")] [Required] IReadOnlyList<string> OrderedExternalIds
);

public sealed record MoveChatRequest(
	[property: JsonPropertyName("project_external_id")] string? ProjectExternalId
);

public sealed record SetIconRequest(
	[property: JsonPropertyName("mime")] [Required, MaxLength(64)] string Mime,
	[property: JsonPropertyName("blob_base64")] [Required]         string BlobBase64,
	[property: JsonPropertyName("source")] [Required, MaxLength(8)] string Source
);
