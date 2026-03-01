using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("contact_identifier")]
public sealed class ContactIdentifierEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Column("contact_id")]
	[JsonPropertyName("contact_id")]
	public long ContactId { get; set; }

	[Required]
	[MaxLength(32)]
	[Column("channel", TypeName = "varchar(32)")]
	[JsonPropertyName("channel")]
	public required string Channel { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	[ForeignKey(nameof(ContactId))]
	public ContactEntity Contact { get; set; } = null!;
}

public class ContactIdentifierEntityConfiguration :IEntityTypeConfiguration<ContactIdentifierEntity> {
	public void Configure(EntityTypeBuilder<ContactIdentifierEntity> builder) {
		builder.HasIndex(e => new { e.Channel, e.ExternalId }).IsUnique();

		builder.HasOne(e => e.Contact)
			   .WithMany(c => c.Identifiers)
			   .HasForeignKey(e => e.ContactId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
