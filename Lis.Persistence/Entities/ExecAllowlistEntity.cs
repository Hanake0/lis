using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("exec_allowlist")]
public sealed class ExecAllowlistEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Column("agent_id")]
	[JsonPropertyName("agent_id")]
	public long? AgentId { get; set; }

	[Required]
	[Column("pattern")]
	[JsonPropertyName("pattern")]
	public required string Pattern { get; set; }

	[Column("last_used_at")]
	[JsonPropertyName("last_used_at")]
	public DateTimeOffset? LastUsedAt { get; set; }

	[Column("last_command")]
	[JsonPropertyName("last_command")]
	public string? LastCommand { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	public AgentEntity? Agent { get; set; }
}

public class ExecAllowlistEntityConfiguration : IEntityTypeConfiguration<ExecAllowlistEntity> {
	public void Configure(EntityTypeBuilder<ExecAllowlistEntity> builder) {
		builder.HasIndex(e => new { e.AgentId, e.Pattern }).IsUnique();

		builder.HasOne(e => e.Agent)
			   .WithMany()
			   .HasForeignKey(e => e.AgentId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
