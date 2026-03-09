using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("chat")]
public sealed class ChatEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	[MaxLength(256)]
	[Column("name", TypeName = "varchar(256)")]
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[Column("is_group")]
	[JsonPropertyName("is_group")]
	public bool IsGroup { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	[Column("current_session_id")]
	[JsonPropertyName("current_session_id")]
	public long? CurrentSessionId { get; set; }

	[Column("agent_id")]
	[JsonPropertyName("agent_id")]
	public long? AgentId { get; set; }

	[Column("enabled")]
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;

	[Column("require_mention")]
	[JsonPropertyName("require_mention")]
	public bool RequireMention { get; set; }

	public SessionEntity? CurrentSession { get; set; }

	public AgentEntity? Agent { get; set; }

	public ICollection<MessageEntity> Messages { get; set; } = [];

	public ICollection<ChatAllowedSenderEntity> AllowedSenders { get; set; } = [];
}

public class ChatEntityConfiguration : IEntityTypeConfiguration<ChatEntity> {
	public void Configure(EntityTypeBuilder<ChatEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();

		builder.HasOne(e => e.CurrentSession)
			   .WithMany()
			   .HasForeignKey(e => e.CurrentSessionId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasOne(e => e.Agent)
			   .WithMany()
			   .HasForeignKey(e => e.AgentId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
