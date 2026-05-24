using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// Per-project key/value store for client-owned metadata that doesn't deserve its
/// own first-class column on <see cref="ProjectEntity"/> — project notes, todo lists,
/// worktree setup commands, plan-file content, sidebar collapse state per-row, etc.
///
/// One row per (project, key) so a notes update doesn't have to round-trip the rest
/// of the bag. Cascade-deletes with the parent project.
/// </summary>
[Table("project_metadata")]
public sealed class ProjectMetadataEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[Column("project_id")]
	[JsonPropertyName("project_id")]
	public required long ProjectId { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("key", TypeName = "varchar(64)")]
	[JsonPropertyName("key")]
	public required string Key { get; set; }

	/// <summary>Free-form string. Callers typically store JSON; column is text so size isn't a worry.</summary>
	[Required]
	[Column("value", TypeName = "text")]
	[JsonPropertyName("value")]
	public required string Value { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ProjectEntity? Project { get; set; }
}

public sealed class ProjectMetadataEntityConfiguration :IEntityTypeConfiguration<ProjectMetadataEntity> {
	public void Configure(EntityTypeBuilder<ProjectMetadataEntity> builder) {
		builder.HasIndex(e => new { e.ProjectId, e.Key }).IsUnique();

		builder.HasOne(e => e.Project)
			.WithMany()
			.HasForeignKey(e => e.ProjectId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
