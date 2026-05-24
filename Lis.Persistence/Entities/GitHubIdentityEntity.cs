using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lis.Persistence.Entities;

/// <summary>
/// One GitHub PAT (personal access token) stored encrypted. Single-row today
/// (single-user Lis); the <c>user_id</c> column is reserved for future multi-user
/// expansion. Token is stored as a string already protected by
/// <c>IDataProtectionProvider</c>, never as plaintext.
/// </summary>
[Table("github_identity")]
public sealed class GitHubIdentityEntity {
	[Key]
	[Column("id")]
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[Column("user_id")]
	[JsonPropertyName("user_id")]
	public long? UserId { get; set; }

	[Required]
	[MaxLength(128)]
	[Column("login", TypeName = "varchar(128)")]
	[JsonPropertyName("login")]
	public required string Login { get; set; }

	[Required]
	[Column("token_protected", TypeName = "text")]
	[JsonIgnore]
	public required string TokenProtected { get; set; }

	[MaxLength(1024)]
	[Column("scopes", TypeName = "varchar(1024)")]
	[JsonPropertyName("scopes")]
	public string? Scopes { get; set; }

	[Column("added_at")]
	[JsonPropertyName("added_at")]
	public DateTimeOffset AddedAt { get; set; }

	[Column("updated_at")]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GitHubIdentityEntityConfiguration :IEntityTypeConfiguration<GitHubIdentityEntity> {
	public void Configure(EntityTypeBuilder<GitHubIdentityEntity> builder) {
		// One row per user; user_id NULL only allowed for the single-user case.
		builder.HasIndex(e => e.UserId).IsUnique();
	}
}
