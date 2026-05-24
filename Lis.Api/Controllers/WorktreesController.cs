using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Git;
using Lis.Core.Events;
using Lis.Git.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lis.Api.Controllers;

/// <summary>
/// REST surface for git worktrees. Creation and deletion publish
/// <c>git_status_changed</c> events on <c>/api/events</c> so sidebars can refresh
/// their worktree list without polling.
/// </summary>
[ApiController]
[Route("api/worktrees")]
[Tags("Worktrees")]
[Produces("application/json")]
public sealed class WorktreesController(
	IGitService  git,
	IEventBus    events,
	TimeProvider time
) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(WorktreeMetadataDto[]), StatusCodes.Status200OK)]
	public async Task<ActionResult<WorktreeMetadataDto[]>> List(
		[FromQuery(Name = "project_path"), BindRequired] string projectPath,
		CancellationToken                                        ct
	) {
		IReadOnlyList<WorktreeMetadata> wts = await git.ListWorktreesAsync(projectPath, ct);
		return this.Ok(wts.Select(WorktreeMetadataDto.From).ToArray());
	}

	[HttpPost]
	[ProducesResponseType(typeof(WorktreeMetadataDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<WorktreeMetadataDto>> Create([FromBody] CreateWorktreeRequest request, CancellationToken ct) {
		WorktreeMetadata wt = await git.CreateWorktreeAsync(request.ProjectPath, request.NewPath, request.Branch, ct);
		WorktreeMetadataDto dto = WorktreeMetadataDto.From(wt);

		await this.PublishAsync(GitEventNames.StatusChanged, new GitStatusChangedPayload(request.ProjectPath), ct);
		return this.Created(string.Empty, dto);
	}

	[HttpDelete]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Remove([FromBody] RemoveWorktreeRequest request, CancellationToken ct) {
		await git.RemoveWorktreeAsync(request.ProjectPath, request.WorktreePath, request.Force, ct);
		await this.PublishAsync(GitEventNames.StatusChanged, new GitStatusChangedPayload(request.ProjectPath), ct);
		return this.NoContent();
	}

	private ValueTask PublishAsync(string name, object data, CancellationToken ct) =>
		events.PublishAsync(new ServerEvent(0, name, data, time.GetUtcNow()), ct);
}
