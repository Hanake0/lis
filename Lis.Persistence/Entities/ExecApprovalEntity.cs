using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("exec_approval")]
public sealed class ExecApprovalEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(16)]
	[Column("approval_id", TypeName = "varchar(16)")]
	[JsonPropertyName("approval_id")]
	public required string ApprovalId { get; set; }

	[Column("chat_id")]
	[JsonPropertyName("chat_id")]
	public long ChatId { get; set; }

	[Column("agent_id")]
	[JsonPropertyName("agent_id")]
	public long? AgentId { get; set; }

	[Required]
	[Column("command")]
	[JsonPropertyName("command")]
	public required string Command { get; set; }

	[Column("cwd")]
	[JsonPropertyName("cwd")]
	public string? Cwd { get; set; }

	[Required]
	[MaxLength(16)]
	[Column("status", TypeName = "varchar(16)")]
	[JsonPropertyName("status")]
	public string Status { get; set; } = "pending";

	[MaxLength(16)]
	[Column("decision", TypeName = "varchar(16)")]
	[JsonPropertyName("decision")]
	public string? Decision { get; set; }

	[MaxLength(64)]
	[Column("resolved_by", TypeName = "varchar(64)")]
	[JsonPropertyName("resolved_by")]
	public string? ResolvedBy { get; set; }

	[MaxLength(128)]
	[Column("message_external_id", TypeName = "varchar(128)")]
	[JsonPropertyName("message_external_id")]
	public string? MessageExternalId { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("expires_at")]
	[JsonPropertyName("expires_at")]
	public DateTimeOffset ExpiresAt { get; set; }

	[Column("resolved_at")]
	[JsonPropertyName("resolved_at")]
	public DateTimeOffset? ResolvedAt { get; set; }

	public ChatEntity? Chat { get; set; }

	public AgentEntity? Agent { get; set; }
}

public class ExecApprovalEntityConfiguration : IEntityTypeConfiguration<ExecApprovalEntity> {
	public void Configure(EntityTypeBuilder<ExecApprovalEntity> builder) {
		builder.HasIndex(e => e.ApprovalId).IsUnique();
		builder.HasIndex(e => e.Status).HasFilter("status = 'pending'");
		builder.HasIndex(e => e.MessageExternalId);

		builder.HasOne(e => e.Chat)
			   .WithMany()
			   .HasForeignKey(e => e.ChatId)
			   .OnDelete(DeleteBehavior.Cascade);

		builder.HasOne(e => e.Agent)
			   .WithMany()
			   .HasForeignKey(e => e.AgentId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
