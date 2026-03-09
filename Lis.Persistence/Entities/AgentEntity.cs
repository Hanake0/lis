using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("agent")]
public sealed class AgentEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("name", TypeName = "varchar(64)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[MaxLength(128)]
	[Column("display_name", TypeName = "varchar(128)")]
	[JsonPropertyName("display_name")]
	public string? DisplayName { get; set; }

	// Model config

	[Required]
	[MaxLength(128)]
	[Column("model", TypeName = "varchar(128)")]
	[JsonPropertyName("model")]
	public required string Model { get; set; }

	[Column("max_tokens")]
	[JsonPropertyName("max_tokens")]
	public int MaxTokens { get; set; } = 4096;

	[Column("context_budget")]
	[JsonPropertyName("context_budget")]
	public int ContextBudget { get; set; } = 12000;

	[MaxLength(16)]
	[Column("thinking_effort", TypeName = "varchar(16)")]
	[JsonPropertyName("thinking_effort")]
	public string? ThinkingEffort { get; set; }

	// Behavior

	[Column("tool_notifications")]
	[JsonPropertyName("tool_notifications")]
	public bool ToolNotifications { get; set; } = true;

	// Compaction config

	[Column("compaction_threshold")]
	[JsonPropertyName("compaction_threshold")]
	public int CompactionThreshold { get; set; }

	[Column("keep_recent_tokens")]
	[JsonPropertyName("keep_recent_tokens")]
	public int KeepRecentTokens { get; set; } = 4000;

	[Column("tool_prune_threshold")]
	[JsonPropertyName("tool_prune_threshold")]
	public int ToolPruneThreshold { get; set; } = 8000;

	[Column("tool_keep_threshold")]
	[JsonPropertyName("tool_keep_threshold")]
	public int ToolKeepThreshold { get; set; } = 2000;

	[MaxLength(16)]
	[Column("tool_summarization_policy", TypeName = "varchar(16)")]
	[JsonPropertyName("tool_summarization_policy")]
	public string? ToolSummarizationPolicy { get; set; }

	[Column("is_default")]
	[JsonPropertyName("is_default")]
	public bool IsDefault { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ICollection<PromptSectionEntity> PromptSections { get; set; } = [];
}

public class AgentEntityConfiguration : IEntityTypeConfiguration<AgentEntity> {
	public void Configure(EntityTypeBuilder<AgentEntity> builder) {
		builder.HasIndex(e => e.Name).IsUnique();
	}
}
