using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pgvector;

namespace Lis.Persistence.Entities;

[Table("session")]
public sealed class SessionEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Column("chat_id")]
	[JsonPropertyName("chat_id")]
	public long ChatId { get; set; }

	[Column("parent_session_id")]
	[JsonPropertyName("parent_session_id")]
	public long? ParentSessionId { get; set; }

	[Column("tools_pruned_through_id")]
	[JsonPropertyName("tools_pruned_through_id")]
	public long? ToolsPrunedThroughId { get; set; }

	[Column("is_compacting")]
	[JsonPropertyName("is_compacting")]
	public bool IsCompacting { get; set; }

	[Column("summary")]
	[JsonPropertyName("summary")]
	public string? Summary { get; set; }

	[Column("summary_embedding", TypeName = "vector(1536)")]
	[JsonPropertyName("summary_embedding")]
	public Vector? SummaryEmbedding { get; set; }

	[Column("total_input_tokens")]
	[JsonPropertyName("total_input_tokens")]
	public long TotalInputTokens { get; set; }

	[Column("total_output_tokens")]
	[JsonPropertyName("total_output_tokens")]
	public long TotalOutputTokens { get; set; }

	[Column("total_cache_read_tokens")]
	[JsonPropertyName("total_cache_read_tokens")]
	public long TotalCacheReadTokens { get; set; }

	[Column("total_cache_creation_tokens")]
	[JsonPropertyName("total_cache_creation_tokens")]
	public long TotalCacheCreationTokens { get; set; }

	[Column("total_thinking_tokens")]
	[JsonPropertyName("total_thinking_tokens")]
	public long TotalThinkingTokens { get; set; }

	/// <summary>
	/// Last API response's total context window size (input + cache_read + cache_creation).
	/// Used for status display, pre-send validation, and compaction notification.
	/// </summary>
	[Column("context_tokens")]
	[JsonPropertyName("context_tokens")]
	public long ContextTokens { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	public ChatEntity Chat { get; set; } = null!;

	public SessionEntity? ParentSession { get; set; }
}

public class SessionEntityConfiguration : IEntityTypeConfiguration<SessionEntity> {
	public void Configure(EntityTypeBuilder<SessionEntity> builder) {
		builder.HasIndex(e => e.ChatId);

		builder.HasOne(e => e.Chat)
			   .WithMany()
			   .HasForeignKey(e => e.ChatId)
			   .OnDelete(DeleteBehavior.Cascade);

		builder.HasOne(e => e.ParentSession)
			   .WithMany()
			   .HasForeignKey(e => e.ParentSessionId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(e => e.SummaryEmbedding)
			   .HasMethod("hnsw")
			   .HasOperators("vector_cosine_ops");
	}
}
