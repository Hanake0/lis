using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// A user-defined grouping of chats. Mirrors OC's ProjectEntry semantically:
/// optional filesystem path, label, icon (emoji or icon name), uploaded icon
/// image blob, accent color, ordering. Chats reference projects via <c>project_id</c>;
/// deleting a project sets that FK to NULL on its chats rather than cascading.
/// </summary>
[Table("project")]
public sealed class ProjectEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	/// <summary>Stable opaque id exposed to clients (path hash or random). Unique.</summary>
	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	[Required]
	[MaxLength(1024)]
	[Column("path", TypeName = "varchar(1024)")]
	[JsonPropertyName("path")]
	public required string Path { get; set; }

	[MaxLength(256)]
	[Column("label", TypeName = "varchar(256)")]
	[JsonPropertyName("label")]
	public string? Label { get; set; }

	[MaxLength(64)]
	[Column("icon", TypeName = "varchar(64)")]
	[JsonPropertyName("icon")]
	public string? Icon { get; set; }

	[MaxLength(64)]
	[Column("icon_image_mime", TypeName = "varchar(64)")]
	[JsonPropertyName("icon_image_mime")]
	public string? IconImageMime { get; set; }

	[Column("icon_image_blob")]
	[JsonIgnore] // never returned inline; served via /api/projects/{id}/icon if needed
	public byte[]? IconImageBlob { get; set; }

	[MaxLength(8)]
	[Column("icon_image_source", TypeName = "varchar(8)")]
	[JsonPropertyName("icon_image_source")]
	public string? IconImageSource { get; set; } // 'custom' | 'auto'

	[Column("icon_image_updated_at")]
	[JsonPropertyName("icon_image_updated_at")]
	public DateTimeOffset? IconImageUpdatedAt { get; set; }

	[MaxLength(16)]
	[Column("icon_background", TypeName = "varchar(16)")]
	[JsonPropertyName("icon_background")]
	public string? IconBackground { get; set; }

	[MaxLength(32)]
	[Column("color", TypeName = "varchar(32)")]
	[JsonPropertyName("color")]
	public string? Color { get; set; }

	[Column("added_at")]
	[JsonPropertyName("added_at")]
	public DateTimeOffset AddedAt { get; set; }

	[Column("last_opened_at")]
	[JsonPropertyName("last_opened_at")]
	public DateTimeOffset? LastOpenedAt { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	[Column("sidebar_collapsed")]
	[JsonPropertyName("sidebar_collapsed")]
	public bool SidebarCollapsed { get; set; }

	[Column("sort_index")]
	[JsonPropertyName("sort_index")]
	public int SortIndex { get; set; }

	public ICollection<ChatEntity> Chats { get; set; } = [];
}

public sealed class ProjectEntityConfiguration :IEntityTypeConfiguration<ProjectEntity> {
	public void Configure(EntityTypeBuilder<ProjectEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();
		builder.HasIndex(e => e.SortIndex);
	}
}
