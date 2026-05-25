using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

[Table("chat")]
public sealed class ChatEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	[MaxLength(256)]
	[Column("name", TypeName = "varchar(256)")]
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[Column("is_group")]
	[JsonPropertyName("is_group")]
	public bool IsGroup { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	[Column("current_session_id")]
	[JsonPropertyName("current_session_id")]
	public long? CurrentSessionId { get; set; }

	[Column("agent_id")]
	[JsonPropertyName("agent_id")]
	public long? AgentId { get; set; }

	[MaxLength(32)]
	[Column("channel", TypeName = "varchar(32)")]
	[JsonPropertyName("channel")]
	public string? Channel { get; set; }

	[Column("archived_at")]
	[JsonPropertyName("archived_at")]
	public DateTimeOffset? ArchivedAt { get; set; }

	[Column("enabled")]
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;

	[Column("require_mention")]
	[JsonPropertyName("require_mention")]
	public bool RequireMention { get; set; }

	[Column("open_group")]
	[JsonPropertyName("open_group")]
	public bool OpenGroup { get; set; }

	[Column("group_context_messages")]
	[JsonPropertyName("group_context_messages")]
	public int? GroupContextMessages { get; set; }

	[Column("debounce_ms")]
	[JsonPropertyName("debounce_ms")]
	public int? DebounceMs { get; set; }

	[MaxLength(512)]
	[Column("group_topic", TypeName = "varchar(512)")]
	[JsonPropertyName("group_topic")]
	public string? GroupTopic { get; set; }

	[Column("project_id")]
	[JsonPropertyName("project_id")]
	public long? ProjectId { get; set; }

	/// <summary>
	/// Per-chat working directory the agent's tools (read, bash, git) treat as
	/// cwd. <c>null</c> means "use the project root" (or the channel's default
	/// when the chat has no project either). The webview composer's worktree
	/// pill writes here via <c>PATCH /api/chats/{id}</c>.
	/// </summary>
	[MaxLength(1024)]
	[Column("directory", TypeName = "varchar(1024)")]
	[JsonPropertyName("directory")]
	public string? Directory { get; set; }

	[Column("pinned_at")]
	[JsonPropertyName("pinned_at")]
	public DateTimeOffset? PinnedAt { get; set; }

	public SessionEntity? CurrentSession { get; set; }

	public AgentEntity? Agent { get; set; }

	public ProjectEntity? Project { get; set; }

	public ICollection<MessageEntity> Messages { get; set; } = [];

	public ICollection<ChatAllowedSenderEntity> AllowedSenders { get; set; } = [];
}

public class ChatEntityConfiguration : IEntityTypeConfiguration<ChatEntity> {
	public void Configure(EntityTypeBuilder<ChatEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();

		builder.HasOne(e => e.CurrentSession)
			   .WithMany()
			   .HasForeignKey(e => e.CurrentSessionId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasOne(e => e.Agent)
			   .WithMany()
			   .HasForeignKey(e => e.AgentId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasOne(e => e.Project)
			   .WithMany(p => p.Chats)
			   .HasForeignKey(e => e.ProjectId)
			   .OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(e => e.ProjectId);
		builder.HasIndex(e => e.PinnedAt);
	}
}
