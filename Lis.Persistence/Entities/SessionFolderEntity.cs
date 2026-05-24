using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// A user-defined folder for grouping chats inside a sidebar scope. Mirrors OC's
/// <c>SessionFolder</c>: each folder belongs to one <c>ScopeKey</c> (e.g.
/// <c>dir:/path/to/project</c>, <c>__archived__:/path</c>, or <c>__global__</c>),
/// can nest under another folder via <see cref="ParentExternalId"/>, and contains
/// chats through the join table <see cref="SessionFolderItemEntity"/>.
/// The backend is scope-agnostic — clients decide what string goes in
/// <see cref="ScopeKey"/> and query by it.
/// </summary>
[Table("session_folder")]
public sealed class SessionFolderEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	/// <summary>Stable opaque id exposed to clients. Unique.</summary>
	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	[Required]
	[MaxLength(512)]
	[Column("scope_key", TypeName = "varchar(512)")]
	[JsonPropertyName("scope_key")]
	public required string ScopeKey { get; set; }

	[Required]
	[MaxLength(256)]
	[Column("name", TypeName = "varchar(256)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	/// <summary>External id of the parent folder. Null = top-level inside the scope.</summary>
	[MaxLength(64)]
	[Column("parent_external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("parent_external_id")]
	public string? ParentExternalId { get; set; }

	[Column("sort_index")]
	[JsonPropertyName("sort_index")]
	public int SortIndex { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ICollection<SessionFolderItemEntity> Items { get; set; } = [];
}

public sealed class SessionFolderEntityConfiguration :IEntityTypeConfiguration<SessionFolderEntity> {
	public void Configure(EntityTypeBuilder<SessionFolderEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();
		// Composite index on (scope, parent, sort) is what the sidebar query orders by.
		builder.HasIndex(e => new { e.ScopeKey, e.ParentExternalId, e.SortIndex });
	}
}
