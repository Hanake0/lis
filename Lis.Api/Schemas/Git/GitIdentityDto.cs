using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Git.Services;
using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Git;

/// <summary>
/// Wire representation of a user-managed git identity profile. Mirrors OC's
/// <c>GitIdentityProfile</c> shape with snake_case keys so byte-identical OC
/// consumers (GitPage, GitIdentityEditorDialog) deserialise unchanged.
/// </summary>
public sealed record GitIdentityDto(
	[property: JsonPropertyName("id")]            string  Id,
	[property: JsonPropertyName("name")]          string  Name,
	[property: JsonPropertyName("user_name")]     string  UserName,
	[property: JsonPropertyName("user_email")]    string  UserEmail,
	[property: JsonPropertyName("auth_type")]     string? AuthType,
	[property: JsonPropertyName("ssh_key_path")]  string? SshKeyPath,
	[property: JsonPropertyName("host")]          string? Host,
	[property: JsonPropertyName("color")]         string? Color,
	[property: JsonPropertyName("icon")]          string? Icon,
	[property: JsonPropertyName("created_at")]    DateTimeOffset CreatedAt,
	[property: JsonPropertyName("updated_at")]    DateTimeOffset UpdatedAt
) {
	public static GitIdentityDto From(GitIdentityEntity e) => new(
		Id:         e.ExternalId,
		Name:       e.Name,
		UserName:   e.UserName,
		UserEmail:  e.UserEmail,
		AuthType:   e.AuthType,
		SshKeyPath: e.SshKeyPath,
		Host:       e.Host,
		Color:      e.Color,
		Icon:       e.Icon,
		CreatedAt:  e.CreatedAt,
		UpdatedAt:  e.UpdatedAt
	);
}

public sealed record CreateGitIdentityRequest(
	[property: JsonPropertyName("id"), MaxLength(64)]                     string? Id,
	[property: JsonPropertyName("name"), Required, MaxLength(128)]        string  Name,
	[property: JsonPropertyName("user_name"), Required, MaxLength(128)]   string  UserName,
	[property: JsonPropertyName("user_email"), Required, MaxLength(256)]  string  UserEmail,
	[property: JsonPropertyName("auth_type"), MaxLength(8)]               string? AuthType,
	[property: JsonPropertyName("ssh_key_path"), MaxLength(1024)]         string? SshKeyPath,
	[property: JsonPropertyName("host"), MaxLength(128)]                  string? Host,
	[property: JsonPropertyName("color"), MaxLength(32)]                  string? Color,
	[property: JsonPropertyName("icon"), MaxLength(64)]                   string? Icon
);

public sealed record UpdateGitIdentityRequest(
	[property: JsonPropertyName("name"), MaxLength(128)]        string? Name,
	[property: JsonPropertyName("user_name"), MaxLength(128)]   string? UserName,
	[property: JsonPropertyName("user_email"), MaxLength(256)]  string? UserEmail,
	[property: JsonPropertyName("auth_type"), MaxLength(8)]     string? AuthType,
	[property: JsonPropertyName("ssh_key_path"), MaxLength(1024)] string? SshKeyPath,
	[property: JsonPropertyName("host"), MaxLength(128)]        string? Host,
	[property: JsonPropertyName("color"), MaxLength(32)]        string? Color,
	[property: JsonPropertyName("icon"), MaxLength(64)]         string? Icon
);

/// <summary>Read-only snapshot of the user's host-wide git config identity.</summary>
public sealed record GlobalGitIdentityDto(
	[property: JsonPropertyName("user_name")]   string? UserName,
	[property: JsonPropertyName("user_email")]  string? UserEmail,
	[property: JsonPropertyName("ssh_command")] string? SshCommand
) {
	public static GlobalGitIdentityDto From(GlobalGitIdentity g) =>
		new(g.UserName, g.UserEmail, g.SshCommand);
}

public sealed record DefaultGitIdentityDto(
	[property: JsonPropertyName("external_id")] string? ExternalId
);

public sealed record SetDefaultGitIdentityRequest(
	[property: JsonPropertyName("external_id"), MaxLength(64)] string? ExternalId
);
