using Lis.Api.Schemas.Git;
using Lis.Git.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lis.Api.Controllers;

/// <summary>
/// Read/write git operations against a local working tree (project root OR linked worktree).
/// All paths come in via query/body — the same controller serves both, since LibGit2Sharp
/// resolves the repo from whatever path is passed in.
/// </summary>
[ApiController]
[Route("api/git")]
[Tags("Git")]
[Produces("application/json")]
public sealed class GitController(IGitService git, TimeProvider time) :ControllerBase {

	// ── Status / branches (read-only) ──────────────────────────────

	[HttpGet("status")]
	[ProducesResponseType(typeof(RepoStatusDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<RepoStatusDto>> Status(
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                                ct
	) {
		RepoStatus status = await git.GetRepoStatusAsync(path, ct);
		return this.Ok(RepoStatusDto.From(status));
	}

	[HttpGet("branches")]
	[ProducesResponseType(typeof(BranchListingDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<BranchListingDto>> Branches(
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                                ct
	) {
		BranchListing listing = await git.ListBranchesAsync(path, ct);
		return this.Ok(BranchListingDto.From(listing));
	}

	// ── Branch CRUD ────────────────────────────────────────────────

	[HttpPost("branches")]
	[ProducesResponseType(typeof(BranchRefDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<ActionResult<BranchRefDto>> CreateBranch(
		[FromBody] CreateBranchRequest request,
		CancellationToken                ct
	) {
		try {
			BranchRef created = await git.CreateBranchAsync(request.Path, request.Name, request.StartPoint, ct);
			return this.StatusCode(StatusCodes.Status201Created, BranchRefDto.From(created));
		} catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
			return this.Conflict(new ProblemDetails {
				Title = "branch already exists",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		}
	}

	[HttpPatch("branches/{name}")]
	[ProducesResponseType(typeof(BranchRefDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<ActionResult<BranchRefDto>> RenameBranch(
		[FromRoute] string              name,
		[FromBody]  RenameBranchRequest request,
		CancellationToken               ct
	) {
		try {
			BranchRef renamed = await git.RenameBranchAsync(request.Path, name, request.NewName, ct);
			return this.Ok(BranchRefDto.From(renamed));
		} catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
			return this.Conflict(new ProblemDetails {
				Title  = "branch already exists",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		}
	}

	[HttpDelete("branches/{name}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<IActionResult> DeleteBranch(
		[FromRoute] string                       name,
		[FromQuery(Name = "path"), BindRequired] string path,
		[FromQuery(Name = "force")]              bool   force,
		CancellationToken                        ct
	) {
		try {
			await git.DeleteBranchAsync(path, name, force, ct);
			return this.NoContent();
		} catch (InvalidOperationException ex) when (ex.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase)) {
			return this.Conflict(new ProblemDetails {
				Title  = "branch not fully merged",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		} catch (InvalidOperationException ex) {
			return this.Conflict(new ProblemDetails {
				Title  = "cannot delete branch",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		}
	}

	// ── Stash ──────────────────────────────────────────────────────

	[HttpGet("stashes")]
	[ProducesResponseType(typeof(StashListDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<StashListDto>> ListStashes(
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                                ct
	) {
		IReadOnlyList<StashEntryInfo> stashes = await git.ListStashesAsync(path, ct);
		DateTimeOffset                now     = time.GetUtcNow();
		StashEntryDto[] dtos = stashes.Select(s => StashEntryDto.From(s, now)).ToArray();
		return this.Ok(new StashListDto(dtos));
	}

	[HttpPost("stashes")]
	[ProducesResponseType(typeof(StashSaveResultDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),     StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<StashSaveResultDto>> CreateStash(
		[FromBody] SaveStashRequest request,
		CancellationToken            ct
	) {
		StashSaveResult result = await git.SaveStashAsync(request.Path, request.Message, request.IncludeUntracked, ct);
		return this.Ok(StashSaveResultDto.From(result));
	}

	[HttpPost("stashes/file-counts")]
	[ProducesResponseType(typeof(StashFileCountsDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<StashFileCountsDto>> CountStashFiles(
		[FromBody] StashFileCountsRequest request,
		CancellationToken                  ct
	) {
		IReadOnlyDictionary<string, int> counts = await git.CountStashFilesAsync(request.Path, request.Refs, ct);
		return this.Ok(new StashFileCountsDto(counts));
	}

	[HttpPost("stashes/{stashRef}/apply")]
	[ProducesResponseType(typeof(StashOpResultDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status409Conflict)]
	public async Task<ActionResult<StashOpResultDto>> ApplyStash(
		[FromRoute] string                       stashRef,
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                        ct
	) {
		try {
			await git.ApplyStashAsync(path, stashRef, ct);
			return this.Ok(new StashOpResultDto(Success: true, stashRef));
		} catch (InvalidOperationException ex) {
			return this.Conflict(new ProblemDetails {
				Title  = "stash apply failed",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		}
	}

	[HttpPost("stashes/{stashRef}/pop")]
	[ProducesResponseType(typeof(StashOpResultDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status409Conflict)]
	public async Task<ActionResult<StashOpResultDto>> PopStash(
		[FromRoute] string                       stashRef,
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                        ct
	) {
		try {
			await git.PopStashAsync(path, stashRef, ct);
			return this.Ok(new StashOpResultDto(Success: true, stashRef));
		} catch (InvalidOperationException ex) {
			return this.Conflict(new ProblemDetails {
				Title  = "stash pop failed",
				Detail = ex.Message,
				Status = StatusCodes.Status409Conflict,
			});
		}
	}

	[HttpDelete("stashes/{stashRef}")]
	[ProducesResponseType(typeof(StashOpResultDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status404NotFound)]
	public async Task<ActionResult<StashOpResultDto>> DropStash(
		[FromRoute] string                       stashRef,
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                        ct
	) {
		await git.DropStashAsync(path, stashRef, ct);
		return this.Ok(new StashOpResultDto(Success: true, stashRef));
	}

	// ── Diff / conflict ────────────────────────────────────────────

	[HttpGet("commits/{commitSha}/file-diff")]
	[ProducesResponseType(typeof(CommitFileDiffDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),    StatusCodes.Status404NotFound)]
	public async Task<ActionResult<CommitFileDiffDto>> CommitFileDiff(
		[FromRoute] string                            commitSha,
		[FromQuery(Name = "path"), BindRequired]      string path,
		[FromQuery(Name = "file"), BindRequired]      string file,
		CancellationToken                             ct
	) {
		CommitFileDiff diff = await git.GetCommitFileDiffAsync(path, commitSha, file, ct);
		return this.Ok(CommitFileDiffDto.From(diff));
	}

	[HttpGet("conflicts")]
	[ProducesResponseType(typeof(MergeConflictDetailsDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<ActionResult<MergeConflictDetailsDto>> Conflicts(
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                                ct
	) {
		MergeConflictDetails? details = await git.GetConflictDetailsAsync(path, ct);
		if (details is null) return this.NoContent();
		return this.Ok(MergeConflictDetailsDto.From(details));
	}

	// ── Worktree bootstrap status ──────────────────────────────────

	[HttpGet("worktree-bootstrap")]
	[ProducesResponseType(typeof(WorktreeBootstrapStatusDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<WorktreeBootstrapStatusDto>> GetWorktreeBootstrap(
		[FromQuery(Name = "path"), BindRequired] string path,
		CancellationToken                                ct
	) {
		WorktreeBootstrapStatus status = await git.GetWorktreeBootstrapStatusAsync(path, ct);
		return this.Ok(WorktreeBootstrapStatusDto.From(status));
	}

	[HttpPut("worktree-bootstrap")]
	[ProducesResponseType(typeof(WorktreeBootstrapStatusDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),             StatusCodes.Status404NotFound)]
	public async Task<ActionResult<WorktreeBootstrapStatusDto>> SetWorktreeBootstrap(
		[FromBody] SetWorktreeBootstrapStatusRequest request,
		CancellationToken                            ct
	) {
		WorktreeBootstrapStatus status = new(request.Status, request.Error, time.GetUtcNow());
		await git.SetWorktreeBootstrapStatusAsync(request.WorktreePath, status, ct);
		return this.Ok(WorktreeBootstrapStatusDto.From(status));
	}
}
