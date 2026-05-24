using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// A locally-managed git identity profile (name/email/SSH key) the user can pick
/// per-project. Mirrors OC's <c>GitIdentityProfile</c>. The token-based variant
/// stores nothing in this row — the actual credential is handled by the OS
/// credential helper or the GitHub identity (<see cref="GitHubIdentityEntity"/>).
///
/// Single-user today (no <c>user_id</c> FK); multi-user expansion would add one.
/// </summary>
[Table("git_identity")]
public sealed class GitIdentityEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Required]
	[MaxLength(64)]
	[Column("external_id", TypeName = "varchar(64)")]
	[JsonPropertyName("external_id")]
	public required string ExternalId { get; set; }

	/// <summary>Display name of the profile (e.g. "Work GitHub").</summary>
	[Required]
	[MaxLength(128)]
	[Column("name", TypeName = "varchar(128)")]
	[JsonPropertyName("name")]
	public required string Name { get; set; }

	/// <summary>git config user.name.</summary>
	[Required]
	[MaxLength(128)]
	[Column("user_name", TypeName = "varchar(128)")]
	[JsonPropertyName("user_name")]
	public required string UserName { get; set; }

	/// <summary>git config user.email.</summary>
	[Required]
	[MaxLength(256)]
	[Column("user_email", TypeName = "varchar(256)")]
	[JsonPropertyName("user_email")]
	public required string UserEmail { get; set; }

	/// <summary>"ssh" or "token"; null = unset.</summary>
	[MaxLength(8)]
	[Column("auth_type", TypeName = "varchar(8)")]
	[JsonPropertyName("auth_type")]
	public string? AuthType { get; set; }

	/// <summary>SSH private key path on disk (when auth_type=ssh). Never the key itself.</summary>
	[MaxLength(1024)]
	[Column("ssh_key_path", TypeName = "varchar(1024)")]
	[JsonPropertyName("ssh_key_path")]
	public string? SshKeyPath { get; set; }

	/// <summary>Hostname this identity targets (github.com / gitlab.com / etc.). Null = any.</summary>
	[MaxLength(128)]
	[Column("host", TypeName = "varchar(128)")]
	[JsonPropertyName("host")]
	public string? Host { get; set; }

	[MaxLength(32)]
	[Column("color", TypeName = "varchar(32)")]
	[JsonPropertyName("color")]
	public string? Color { get; set; }

	[MaxLength(64)]
	[Column("icon", TypeName = "varchar(64)")]
	[JsonPropertyName("icon")]
	public string? Icon { get; set; }

	[Column("created_at")]
	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GitIdentityEntityConfiguration :IEntityTypeConfiguration<GitIdentityEntity> {
	public void Configure(EntityTypeBuilder<GitIdentityEntity> builder) {
		builder.HasIndex(e => e.ExternalId).IsUnique();
	}
}
