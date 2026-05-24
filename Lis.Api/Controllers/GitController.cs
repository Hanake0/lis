using Lis.Api.Schemas.Git;
using Lis.Git.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lis.Api.Controllers;

/// <summary>
/// Read-only git metadata for a local working tree. The path is passed in via query
/// so the same controller serves both project roots and linked worktrees.
/// </summary>
[ApiController]
[Route("api/git")]
[Tags("Git")]
[Produces("application/json")]
public sealed class GitController(IGitService git) :ControllerBase {

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
}
