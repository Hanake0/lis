using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("message")]
public sealed class MessageEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[MaxLength(128)]
	[Column("external_id", TypeName = "varchar(128)")]
	[JsonPropertyName("external_id")]
	public string? ExternalId { get; set; }

	[Column("chat_id")]
	[JsonPropertyName("chat_id")]
	public long ChatId { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("sender_id", TypeName = "varchar(64)")]
	[JsonPropertyName("sender_id")]
	public required string SenderId { get; set; }

	[MaxLength(256)]
	[Column("sender_name", TypeName = "varchar(256)")]
	[JsonPropertyName("sender_name")]
	public string? SenderName { get; set; }

	[Column("is_from_me")]
	[JsonPropertyName("is_from_me")]
	public bool IsFromMe { get; set; }

	[Column("body")]
	[JsonPropertyName("body")]
	public string? Body { get; set; }

	[MaxLength(32)]
	[Column("media_type", TypeName = "varchar(32)")]
	[JsonPropertyName("media_type")]
	public string? MediaType { get; set; }

	[Column("media_caption")]
	[JsonPropertyName("media_caption")]
	public string? MediaCaption { get; set; }

	[MaxLength(128)]
	[Column("reply_to_id", TypeName = "varchar(128)")]
	[JsonPropertyName("reply_to_id")]
	public string? ReplyToId { get; set; }

	[Column("sk_content", TypeName = "jsonb")]
	[JsonPropertyName("sk_content")]
	public string? SkContent { get; set; }

	[Column("token_count")]
	[JsonPropertyName("token_count")]
	public int TokenCount { get; set; }

	[Column("timestamp")]
	[JsonPropertyName("timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[ForeignKey(nameof(ChatId))]
	public ChatEntity Chat { get; set; } = null!;
}

public class MessageEntityConfiguration :IEntityTypeConfiguration<MessageEntity> {
	public void Configure(EntityTypeBuilder<MessageEntity> builder) {
		builder.HasIndex(e => e.ExternalId)
			   .IsUnique()
			   .HasFilter("\"external_id\" IS NOT NULL");

		builder.HasIndex(e => new { e.ChatId, e.Timestamp });

		builder.HasOne(e => e.Chat)
			   .WithMany(c => c.Messages)
			   .HasForeignKey(e => e.ChatId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
