using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Lis.Persistence.Entities;

[Table("contact")]
public sealed class ContactEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[MaxLength(256)]
	[Column("name", TypeName = "varchar(256)")]
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ICollection<ContactIdentifierEntity> Identifiers { get; set; } = [];
	public ICollection<MemoryEntity> Memories { get; set; } = [];
}
