using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Api.Services;

namespace Lis.Api.Schemas.GitHub;

public sealed record GitHubStatusDto(
	[property: JsonPropertyName("connected")] bool                  Connected,
	[property: JsonPropertyName("login")]     string?               Login,
	[property: JsonPropertyName("scopes")]    string?               Scopes,
	[property: JsonPropertyName("user")]      GitHubUserDto?        User,
	[property: JsonPropertyName("accounts")]  IReadOnlyList<GitHubAccountDto> Accounts
) {
	public static GitHubStatusDto From(GitHubStatus s) {
		GitHubUserDto? user = s.Login is null
			? null
			: new GitHubUserDto(Login: s.Login, AvatarUrl: s.AvatarUrl);

		// Lis is single-account today, so `accounts` is the current user as the only
		// (and active) account. OC's switcher hides itself when the list has one entry.
		IReadOnlyList<GitHubAccountDto> accounts = s.Connected && s.Login is not null
			? [new GitHubAccountDto(Id: s.Login, Login: s.Login, AvatarUrl: s.AvatarUrl, IsActive: true)]
			: [];

		return new GitHubStatusDto(s.Connected, s.Login, s.Scopes, user, accounts);
	}
}

public sealed record GitHubUserDto(
	[property: JsonPropertyName("login")]      string  Login,
	[property: JsonPropertyName("avatar_url")] string? AvatarUrl
);

public sealed record GitHubAccountDto(
	[property: JsonPropertyName("id")]         string  Id,
	[property: JsonPropertyName("login")]      string  Login,
	[property: JsonPropertyName("avatar_url")] string? AvatarUrl,
	[property: JsonPropertyName("is_active")]  bool    IsActive
);

public sealed record SetGitHubTokenRequest(
	[property: JsonPropertyName("token"), Required, MaxLength(512)] string Token
);

public sealed record PrKeyDto(
	[property: JsonPropertyName("owner"), Required, MaxLength(128)]  string Owner,
	[property: JsonPropertyName("repo"), Required, MaxLength(256)]   string Repo,
	[property: JsonPropertyName("branch"), Required, MaxLength(256)] string Branch
);

public sealed record DirectoryPrKeyDto(
	[property: JsonPropertyName("directory"), Required, MaxLength(1024)] string Directory,
	[property: JsonPropertyName("branch"), Required, MaxLength(256)]     string Branch
);

public sealed record PrStatusRequest(
	[property: JsonPropertyName("keys"), Required] IReadOnlyList<PrKeyDto> Keys
);

public sealed record PrStatusByDirectoryRequest(
	[property: JsonPropertyName("keys"), Required] IReadOnlyList<DirectoryPrKeyDto> Keys
);

// ── Enriched PR status DTO (matches OC's GitHubPullRequestStatus shape) ────

public sealed record PrRepoRefDto(
	[property: JsonPropertyName("owner")] string Owner,
	[property: JsonPropertyName("repo")]  string Repo
) {
	public static PrRepoRefDto From(PrRepoRef r) => new(r.Owner, r.Repo);
}

public sealed record PullRequestDetailDto(
	[property: JsonPropertyName("number")]          int     Number,
	[property: JsonPropertyName("title")]           string  Title,
	[property: JsonPropertyName("body")]            string? Body,
	[property: JsonPropertyName("url")]             string  Url,
	[property: JsonPropertyName("state")]           string  State,
	[property: JsonPropertyName("draft")]           bool    Draft,
	[property: JsonPropertyName("base")]            string  Base,
	[property: JsonPropertyName("head")]            string  Head,
	[property: JsonPropertyName("head_sha")]        string? HeadSha,
	[property: JsonPropertyName("mergeable")]       bool?   Mergeable,
	[property: JsonPropertyName("mergeable_state")] string? MergeableState
) {
	public static PullRequestDetailDto From(PullRequestDetail d) => new(
		Number:         d.Number,
		Title:          d.Title,
		Body:           d.Body,
		Url:            d.Url,
		State:          d.State,
		Draft:          d.Draft,
		Base:           d.Base,
		Head:           d.Head,
		HeadSha:        d.HeadSha,
		Mergeable:      d.Mergeable,
		MergeableState: d.MergeableState
	);
}

public sealed record ChecksSummaryDto(
	[property: JsonPropertyName("state")]   string? State,
	[property: JsonPropertyName("total")]   int     Total,
	[property: JsonPropertyName("pending")] int     Pending,
	[property: JsonPropertyName("success")] int     Success,
	[property: JsonPropertyName("failure")] int     Failure
) {
	public static ChecksSummaryDto From(ChecksSummary c) => new(c.State, c.Total, c.Pending, c.Success, c.Failure);
}

public sealed record PrStatusDto(
	[property: JsonPropertyName("connected")]            bool                  Connected,
	[property: JsonPropertyName("repo")]                 PrRepoRefDto?         Repo,
	[property: JsonPropertyName("branch")]               string?               Branch,
	[property: JsonPropertyName("pr")]                   PullRequestDetailDto? Pr,
	[property: JsonPropertyName("checks")]               ChecksSummaryDto?     Checks,
	[property: JsonPropertyName("can_merge")]            bool?                 CanMerge,
	[property: JsonPropertyName("default_branch")]       string?               DefaultBranch,
	[property: JsonPropertyName("resolved_remote_name")] string?               ResolvedRemoteName,
	[property: JsonPropertyName("error")]                string?               Error
) {
	public static PrStatusDto From(PrStatus s) => new(
		Connected:          s.Connected,
		Repo:               s.Repo is null    ? null : PrRepoRefDto.From(s.Repo),
		Branch:             s.Branch,
		Pr:                 s.Pr is null      ? null : PullRequestDetailDto.From(s.Pr),
		Checks:             s.Checks is null  ? null : ChecksSummaryDto.From(s.Checks),
		CanMerge:           s.CanMerge,
		DefaultBranch:      s.DefaultBranch,
		ResolvedRemoteName: s.ResolvedRemoteName,
		Error:              s.Error
	);
}

public sealed record PrStatusResponse(
	[property: JsonPropertyName("results")] IReadOnlyDictionary<string, PrStatusDto> Results
);
