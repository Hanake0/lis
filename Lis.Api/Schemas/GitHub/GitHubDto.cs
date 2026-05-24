using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Api.Services;

namespace Lis.Api.Schemas.GitHub;

public sealed record GitHubStatusDto(
	[property: JsonPropertyName("connected")] bool    Connected,
	[property: JsonPropertyName("login")]     string? Login,
	[property: JsonPropertyName("scopes")]    string? Scopes
) {
	public static GitHubStatusDto From(GitHubStatus s) => new(s.Connected, s.Login, s.Scopes);
}

public sealed record SetGitHubTokenRequest(
	[property: JsonPropertyName("token"), Required, MaxLength(512)] string Token
);

public sealed record PrKeyDto(
	[property: JsonPropertyName("owner"), Required, MaxLength(128)]  string Owner,
	[property: JsonPropertyName("repo"), Required, MaxLength(256)]   string Repo,
	[property: JsonPropertyName("branch"), Required, MaxLength(256)] string Branch
);

public sealed record PrStatusRequest(
	[property: JsonPropertyName("keys"), Required] IReadOnlyList<PrKeyDto> Keys
);

public sealed record PrStatusDto(
	[property: JsonPropertyName("state")]   string? State,
	[property: JsonPropertyName("checks")]  string? Checks,
	[property: JsonPropertyName("reviews")] string? Reviews
) {
	public static PrStatusDto From(PrStatus s) => new(s.State, s.Checks, s.Reviews);
}

public sealed record PrStatusResponse(
	[property: JsonPropertyName("results")] IReadOnlyDictionary<string, PrStatusDto> Results
);
