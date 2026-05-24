using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Git.Services;

namespace Lis.Api.Schemas.Git;

public sealed record RepoStatusDto(
	[property: JsonPropertyName("is_git_repo")] bool          IsGitRepo,
	[property: JsonPropertyName("branch")]      string?       Branch,
	[property: JsonPropertyName("ahead")]       int           Ahead,
	[property: JsonPropertyName("behind")]      int           Behind,
	[property: JsonPropertyName("dirty")]       bool          Dirty,
	[property: JsonPropertyName("last_commit")] CommitInfoDto? LastCommit
) {
	public static RepoStatusDto From(RepoStatus s) => new(
		s.IsGitRepo, s.Branch, s.Ahead, s.Behind, s.Dirty,
		s.LastCommit is null ? null : CommitInfoDto.From(s.LastCommit)
	);
}

public sealed record CommitInfoDto(
	[property: JsonPropertyName("sha")]     string         Sha,
	[property: JsonPropertyName("message")] string         Message,
	[property: JsonPropertyName("author")]  string         Author,
	[property: JsonPropertyName("at")]      DateTimeOffset At
) {
	public static CommitInfoDto From(CommitInfo c) => new(c.Sha, c.Message, c.Author, c.At);
}

public sealed record BranchListingDto(
	[property: JsonPropertyName("local")]   IReadOnlyList<string> Local,
	[property: JsonPropertyName("remote")]  IReadOnlyList<string> Remote,
	[property: JsonPropertyName("current")] string?               Current
) {
	public static BranchListingDto From(BranchListing b) => new(b.Local, b.Remote, b.Current);
}

public sealed record WorktreeMetadataDto(
	[property: JsonPropertyName("name")]        string  Name,
	[property: JsonPropertyName("path")]        string  Path,
	[property: JsonPropertyName("branch")]      string? Branch,
	[property: JsonPropertyName("head")]        string? Head,
	[property: JsonPropertyName("is_locked")]   bool    IsLocked,
	[property: JsonPropertyName("is_prunable")] bool    IsPrunable
) {
	public static WorktreeMetadataDto From(WorktreeMetadata w) =>
		new(w.Name, w.Path, w.Branch, w.Head, w.IsLocked, w.IsPrunable);
}

public sealed record CreateWorktreeRequest(
	[property: JsonPropertyName("project_path"), Required, MaxLength(1024)] string ProjectPath,
	[property: JsonPropertyName("new_path"),    Required, MaxLength(1024)]  string NewPath,
	[property: JsonPropertyName("branch"),      Required, MaxLength(256)]   string Branch
);

public sealed record RemoveWorktreeRequest(
	[property: JsonPropertyName("project_path"),  Required, MaxLength(1024)] string ProjectPath,
	[property: JsonPropertyName("worktree_path"), Required, MaxLength(1024)] string WorktreePath,
	[property: JsonPropertyName("force")]                                    bool   Force
);
