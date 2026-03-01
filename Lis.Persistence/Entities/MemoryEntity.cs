using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pgvector;

namespace Lis.Persistence.Entities;

[Table("memory")]
public sealed class MemoryEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[Column("content")]
	[JsonPropertyName("content")]
	public required string Content { get; set; }

	[Column("contact_id")]
	[JsonPropertyName("contact_id")]
	public long? ContactId { get; set; }

	[ForeignKey(nameof(ContactId))]
	public ContactEntity? Contact { get; set; }

	[Column("embedding", TypeName = "vector(1536)")]
	public Vector? Embedding { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }
}

public class MemoryEntityConfiguration :IEntityTypeConfiguration<MemoryEntity> {
	public void Configure(EntityTypeBuilder<MemoryEntity> builder) {
		builder.HasIndex(e => e.ContactId);

		builder.HasOne(e => e.Contact)
			   .WithMany(c => c.Memories)
			   .HasForeignKey(e => e.ContactId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(e => e.Embedding)
			   .HasMethod("hnsw")
			   .HasOperators("vector_cosine_ops");
	}
}
