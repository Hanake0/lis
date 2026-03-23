using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("cron_job")]
public sealed class CronJobEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(128)]
	[Column("name", TypeName = "varchar(128)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("cron_expression", TypeName = "varchar(64)")]
	[JsonPropertyName("cron_expression")]
	public required string CronExpression { get; set; }

	[Required]
	[MaxLength(256)]
	[Column("handler", TypeName = "varchar(256)")]
	[JsonPropertyName("handler")]
	public required string Handler { get; set; }

	[Column("chat_id")]
	[JsonPropertyName("chat_id")]
	public long ChatId { get; set; }

	[Column("is_deterministic")]
	[JsonPropertyName("is_deterministic")]
	public bool IsDeterministic { get; set; } = true;

	[Column("enabled")]
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;

	[Column("next_run_at")]
	[JsonPropertyName("next_run_at")]
	public DateTimeOffset NextRunAt { get; set; }

	[Column("last_run_at")]
	[JsonPropertyName("last_run_at")]
	public DateTimeOffset? LastRunAt { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ChatEntity Chat { get; set; } = null!;
}

public class CronJobEntityConfiguration : IEntityTypeConfiguration<CronJobEntity> {
	public void Configure(EntityTypeBuilder<CronJobEntity> builder) {
		builder.HasIndex(e => e.ChatId);
		builder.HasIndex(e => new { e.Enabled, e.NextRunAt });

		builder.HasOne(e => e.Chat)
			   .WithMany()
			   .HasForeignKey(e => e.ChatId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
