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
	[property: JsonPropertyName("project_path")] [Required, MaxLength(1024)] string ProjectPath,
	[property: JsonPropertyName("new_path")] [Required, MaxLength(1024)]  string NewPath,
	[property: JsonPropertyName("branch")] [Required, MaxLength(256)]   string Branch
);

public sealed record RemoveWorktreeRequest(
	[property: JsonPropertyName("project_path")] [Required, MaxLength(1024)] string ProjectPath,
	[property: JsonPropertyName("worktree_path")] [Required, MaxLength(1024)] string WorktreePath,
	[property: JsonPropertyName("force")]                                    bool   Force
);

// ── Branch CRUD ──────────────────────────────────────────────────────

public sealed record BranchRefDto(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("tip")]  string Tip
) {
	public static BranchRefDto From(BranchRef r) => new(r.Name, r.Tip);
}

public sealed record CreateBranchRequest(
	[property: JsonPropertyName("path")] [Required, MaxLength(1024)] string  Path,
	[property: JsonPropertyName("name")] [Required, MaxLength(256)]  string  Name,
	[property: JsonPropertyName("start_point")] [MaxLength(256)]     string? StartPoint
);

public sealed record RenameBranchRequest(
	[property: JsonPropertyName("path")] [Required, MaxLength(1024)] string Path,
	[property: JsonPropertyName("new_name")] [Required, MaxLength(256)]  string NewName
);

// ── Stash ────────────────────────────────────────────────────────────

public sealed record StashEntryDto(
	[property: JsonPropertyName("index")]         int            Index,
	[property: JsonPropertyName("ref")]           string         Ref,
	[property: JsonPropertyName("hash")]          string         Hash,
	[property: JsonPropertyName("message")]       string         Message,
	[property: JsonPropertyName("at")]            DateTimeOffset At,
	[property: JsonPropertyName("relative_time")] string         RelativeTime
) {
	public static StashEntryDto From(StashEntryInfo s, DateTimeOffset now) => new(
		s.Index,
		s.Ref,
		s.Hash,
		s.Message,
		s.At,
		FormatRelative(s.At, now)
	);

	private static string FormatRelative(DateTimeOffset at, DateTimeOffset now) {
		TimeSpan span = now - at;
		if (span.TotalSeconds < 60)   return $"{(int)Math.Max(0, span.TotalSeconds)}s ago";
		if (span.TotalMinutes < 60)   return $"{(int)span.TotalMinutes}m ago";
		if (span.TotalHours   < 24)   return $"{(int)span.TotalHours}h ago";
		if (span.TotalDays    < 7)    return $"{(int)span.TotalDays}d ago";
		if (span.TotalDays    < 365)  return $"{(int)(span.TotalDays / 7)}w ago";
		return $"{(int)(span.TotalDays / 365)}y ago";
	}
}

public sealed record StashListDto(
	[property: JsonPropertyName("stashes")] IReadOnlyList<StashEntryDto> Stashes
);

public sealed record SaveStashRequest(
	[property: JsonPropertyName("path")] [Required, MaxLength(1024)] string  Path,
	[property: JsonPropertyName("message")] [MaxLength(1024)]        string? Message,
	[property: JsonPropertyName("include_untracked")]               bool    IncludeUntracked
);

public sealed record StashSaveResultDto(
	[property: JsonPropertyName("success")] bool    Success,
	[property: JsonPropertyName("created")] bool    Created,
	[property: JsonPropertyName("ref")]     string? Ref,
	[property: JsonPropertyName("message")] string  Message,
	[property: JsonPropertyName("output")]  string  Output
) {
	public static StashSaveResultDto From(StashSaveResult r) =>
		new(Success: true, r.Created, r.Ref, r.Message, r.Created ? "stash saved" : r.Message);
}

public sealed record StashOpResultDto(
	[property: JsonPropertyName("success")] bool   Success,
	[property: JsonPropertyName("ref")]     string Ref
);

public sealed record StashFileCountsRequest(
	[property: JsonPropertyName("path")] [Required, MaxLength(1024)] string                Path,
	[property: JsonPropertyName("refs")] [Required]                  IReadOnlyList<string> Refs
);

public sealed record StashFileCountsDto(
	[property: JsonPropertyName("counts")] IReadOnlyDictionary<string, int> Counts
);

// ── Diff / conflict ──────────────────────────────────────────────────

public sealed record CommitFileDiffDto(
	[property: JsonPropertyName("original")]  string Original,
	[property: JsonPropertyName("modified")]  string Modified,
	[property: JsonPropertyName("is_binary")] bool   IsBinary
) {
	public static CommitFileDiffDto From(CommitFileDiff d) => new(d.Original, d.Modified, d.IsBinary);
}

public sealed record MergeConflictDetailsDto(
	[property: JsonPropertyName("operation")]         string                Operation,
	[property: JsonPropertyName("status_porcelain")]  string                StatusPorcelain,
	[property: JsonPropertyName("unmerged_files")]    IReadOnlyList<string> UnmergedFiles,
	[property: JsonPropertyName("diff")]              string                Diff,
	[property: JsonPropertyName("head_info")]         string?               HeadInfo
) {
	public static MergeConflictDetailsDto From(MergeConflictDetails d) =>
		new(d.Operation, d.StatusPorcelain, d.UnmergedFiles, d.Diff, d.HeadInfo);
}

// ── Worktree bootstrap ───────────────────────────────────────────────

public sealed record WorktreeBootstrapStatusDto(
	[property: JsonPropertyName("status")]     string         Status,
	[property: JsonPropertyName("error")]      string?        Error,
	[property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt
) {
	public static WorktreeBootstrapStatusDto From(WorktreeBootstrapStatus s) =>
		new(s.Status, s.Error, s.UpdatedAt);
}

public sealed record SetWorktreeBootstrapStatusRequest(
	[property: JsonPropertyName("worktree_path")] [Required, MaxLength(1024)] string  WorktreePath,
	[property: JsonPropertyName("status")] [Required, MaxLength(16)]   string  Status,
	[property: JsonPropertyName("error")] [MaxLength(2048)] string? Error
);
