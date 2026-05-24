using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// A user-scheduled prompt that fires on a cron expression. The min-heap
/// <c>ScheduledTaskRunner</c> in <c>Lis.Api</c> picks these up at startup and on
/// CRUD events, and triggers the configured executor when their
/// <see cref="NextRunAt"/> elapses.
/// </summary>
[Table("scheduled_task")]
public sealed class ScheduledTaskEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	/// <summary>Optional binding to a project — null tasks fire without a project context.</summary>
	[Column("project_id")]
	[JsonPropertyName("project_id")]
	public long? ProjectId { get; set; }

	[Required]
	[MaxLength(256)]
	[Column("name", TypeName = "varchar(256)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[Required]
	[MaxLength(128)]
	[Column("cron_expr", TypeName = "varchar(128)")]
	[JsonPropertyName("cron_expr")]
	public required string CronExpression { get; set; }

	[Required]
	[Column("prompt", TypeName = "text")]
	[JsonPropertyName("prompt")]
	public required string Prompt { get; set; }

	[MaxLength(64)]
	[Column("agent_name", TypeName = "varchar(64)")]
	[JsonPropertyName("agent_name")]
	public string? AgentName { get; set; }

	[MaxLength(128)]
	[Column("model", TypeName = "varchar(128)")]
	[JsonPropertyName("model")]
	public string? Model { get; set; }

	/// <summary>Optional IANA tz id. Defaults to UTC when null.</summary>
	[MaxLength(64)]
	[Column("timezone", TypeName = "varchar(64)")]
	[JsonPropertyName("timezone")]
	public string? Timezone { get; set; }

	[Column("enabled")]
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;

	[Column("next_run_at")]
	[JsonPropertyName("next_run_at")]
	public DateTimeOffset? NextRunAt { get; set; }

	[Column("last_run_at")]
	[JsonPropertyName("last_run_at")]
	public DateTimeOffset? LastRunAt { get; set; }

	[MaxLength(32)]
	[Column("last_status", TypeName = "varchar(32)")]
	[JsonPropertyName("last_status")]
	public string? LastStatus { get; set; }

	[Column("last_error", TypeName = "text")]
	[JsonPropertyName("last_error")]
	public string? LastError { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ProjectEntity? Project { get; set; }
}

public sealed class ScheduledTaskEntityConfiguration :IEntityTypeConfiguration<ScheduledTaskEntity> {
	public void Configure(EntityTypeBuilder<ScheduledTaskEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();
		// Startup query is "WHERE enabled = true ORDER BY next_run_at" — the index lets the
		// runner load its heap in one seq scan.
		builder.HasIndex(e => new { e.Enabled, e.NextRunAt });

		builder.HasOne(e => e.Project)
			   .WithMany()
			   .HasForeignKey(e => e.ProjectId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
