using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// Single-row-per-key generic settings store. Used for small UX preferences
/// that don't deserve a dedicated entity (default git identity, sidebar
/// preferences toggled via Settings, etc.). Single-user today.
///
/// Use sparingly — anything with structure deserves its own entity. Values
/// are stored as opaque text so consumers must agree on the serialisation
/// per key (typically JSON or a literal string).
/// </summary>
[Table("setting")]
public sealed class SettingEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(128)]
	[Column("key", TypeName = "varchar(128)")]
	[JsonPropertyName("key")]
	public required string Key { get; set; }

	[Required]
	[Column("value", TypeName = "text")]
	[JsonPropertyName("value")]
	public required string Value { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SettingEntityConfiguration :IEntityTypeConfiguration<SettingEntity> {
	public void Configure(EntityTypeBuilder<SettingEntity> builder) {
		builder.HasIndex(e => e.Key).IsUnique();
	}
}
