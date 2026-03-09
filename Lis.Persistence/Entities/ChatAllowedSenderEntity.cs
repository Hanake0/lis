using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("chat_allowed_sender")]
public sealed class ChatAllowedSenderEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Column("chat_id")]
	[JsonPropertyName("chat_id")]
	public long ChatId { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("sender_id", TypeName = "varchar(64)")]
	[JsonPropertyName("sender_id")]
	public required string SenderId { get; set; }

	public ChatEntity Chat { get; set; } = null!;
}

public class ChatAllowedSenderEntityConfiguration : IEntityTypeConfiguration<ChatAllowedSenderEntity> {
	public void Configure(EntityTypeBuilder<ChatAllowedSenderEntity> builder) {
		builder.HasIndex(e => new { e.ChatId, e.SenderId }).IsUnique();
		builder.HasIndex(e => e.ChatId);

		builder.HasOne(e => e.Chat)
			   .WithMany(c => c.AllowedSenders)
			   .HasForeignKey(e => e.ChatId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
