using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// Join row putting a chat into a <see cref="SessionFolderEntity"/>. We key on
/// <c>chat_external_id</c> rather than the chat row id so that folder membership
/// survives even when chats are missing from this DB instance — matches OC's model
/// where folder items can refer to chats that haven't synced yet.
/// </summary>
[Table("session_folder_item")]
[PrimaryKey(nameof(FolderId), nameof(ChatExternalId))]
public sealed class SessionFolderItemEntity {
	[Column("folder_id")]
	[JsonPropertyName("folder_id")]
	public long FolderId { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("chat_external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("chat_external_id")]
	public required string ChatExternalId { get; set; }

	[Column("sort_index")]
	[JsonPropertyName("sort_index")]
	public int SortIndex { get; set; }

	[Column("added_at")]
	[JsonPropertyName("added_at")]
	public DateTimeOffset AddedAt { get; set; }

	public SessionFolderEntity? Folder { get; set; }
}

public sealed class SessionFolderItemEntityConfiguration :IEntityTypeConfiguration<SessionFolderItemEntity> {
	public void Configure(EntityTypeBuilder<SessionFolderItemEntity> builder) {
		builder.HasOne(e => e.Folder)
			   .WithMany(f => f.Items)
			   .HasForeignKey(e => e.FolderId)
			   .OnDelete(DeleteBehavior.Cascade);

		builder.HasIndex(e => e.ChatExternalId);
	}
}
