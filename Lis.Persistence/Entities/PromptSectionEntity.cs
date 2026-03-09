using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("prompt_section")]
public sealed class PromptSectionEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(50)]
	[Column("name", TypeName = "varchar(50)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[Required]
	[Column("content")]
	[JsonPropertyName("content")]
	public required string Content { get; set; }

	[Column("sort_order")]
	[JsonPropertyName("sort_order")]
	public int SortOrder { get; set; }

	[Column("is_enabled")]
	[JsonPropertyName("is_enabled")]
	public bool IsEnabled { get; set; } = true;

	[Column("agent_id")]
	[JsonPropertyName("agent_id")]
	public long AgentId { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public AgentEntity Agent { get; set; } = null!;
}

public class PromptSectionEntityConfiguration : IEntityTypeConfiguration<PromptSectionEntity> {
	public void Configure(EntityTypeBuilder<PromptSectionEntity> builder) {
		builder.HasIndex(e => new { e.AgentId, e.Name }).IsUnique();

		builder.HasOne(e => e.Agent)
			   .WithMany(a => a.PromptSections)
			   .HasForeignKey(e => e.AgentId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
