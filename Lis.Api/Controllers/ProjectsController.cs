using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Projects;
using Lis.Core.Events;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// REST surface for the Project aggregate. Mutations emit SSE events via
/// <see cref="IEventBus"/> so other connected clients refresh their sidebars
/// without polling.
/// </summary>
[ApiController]
[Route("api/projects")]
[Tags("Projects")]
[Produces("application/json")]
public sealed class ProjectsController(
	IProjectService projects,
	IEventBus       events,
	TimeProvider    time
) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(ProjectDto[]), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<ProjectDto[]>> List(CancellationToken ct) {
		IReadOnlyList<ProjectEntity> all = await projects.ListAsync(ct);
		return this.Ok(all.Select(ProjectDto.From).ToArray());
	}

	[HttpGet("{externalId}")]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<ProjectDto>> Get([FromRoute] string externalId, CancellationToken ct) {
		ProjectEntity? project = await projects.GetByExternalIdAsync(externalId, ct);
		return project is null
			? this.NotFound($"Project '{externalId}' not found")
			: this.Ok(ProjectDto.From(project));
	}

	[HttpPost]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<ProjectDto>> Create([FromBody] CreateProjectRequest request, CancellationToken ct) {
		ProjectEntity project = await projects.CreateAsync(request.Path, request.Label, request.Color, ct);
		ProjectDto dto = ProjectDto.From(project);

		await this.PublishAsync(ProjectEventNames.Created, new ProjectCreatedPayload(dto), ct);
		return this.CreatedAtAction(nameof(this.Get), new { externalId = dto.Id }, dto);
	}

	[HttpPatch("{externalId}")]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<ProjectDto>> Update(
		[FromRoute] string externalId,
		[FromBody]  UpdateProjectRequest request,
		CancellationToken ct
	) {
		ProjectEntity project = await projects.UpdateMetaAsync(
			externalId,
			new ProjectMetaUpdate(
				Label:            request.Label,
				Color:            request.Color,
				Icon:             request.Icon,
				IconBackground:   request.IconBackground,
				SidebarCollapsed: request.SidebarCollapsed
			),
			ct
		);
		ProjectDto dto = ProjectDto.From(project);

		await this.PublishAsync(ProjectEventNames.Updated, new ProjectUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	[HttpDelete("{externalId}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> Delete([FromRoute] string externalId, CancellationToken ct) {
		await projects.DeleteAsync(externalId, ct);
		await this.PublishAsync(ProjectEventNames.Deleted, new ProjectDeletedPayload(externalId), ct);
		return this.NoContent();
	}

	[HttpPost("reorder")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> Reorder([FromBody] ReorderProjectsRequest request, CancellationToken ct) {
		await projects.ReorderAsync(request.OrderedExternalIds, ct);
		// Reorder doesn't have a dedicated event; clients re-fetch the list after seeing project_updated for any reordered id.
		// Emit an "updated" event for each reordered id so the sidebar can resync without a full list call.
		IReadOnlyList<ProjectEntity> after = await projects.ListAsync(ct);
		foreach (string id in request.OrderedExternalIds) {
			ProjectEntity? p = after.FirstOrDefault(x => x.ExternalId == id);
			if (p is null) continue;
			await this.PublishAsync(ProjectEventNames.Updated, new ProjectUpdatedPayload(ProjectDto.From(p)), ct);
		}
		return this.NoContent();
	}

	[HttpPost("{externalId}/touch")]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectDto>> Touch([FromRoute] string externalId, CancellationToken ct) {
		ProjectEntity project = await projects.TouchLastOpenedAsync(externalId, ct);
		// Touch is a high-frequency op; intentionally do NOT publish an event for it.
		return this.Ok(ProjectDto.From(project));
	}

	[HttpPost("{externalId}/icon")]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectDto>> SetIcon(
		[FromRoute] string externalId,
		[FromBody]  SetIconRequest request,
		CancellationToken ct
	) {
		byte[] blob;
		try {
			blob = Convert.FromBase64String(request.BlobBase64);
		} catch (FormatException ex) {
			throw new ArgumentException("blob_base64 is not valid base64", nameof(request), ex);
		}

		ProjectEntity project = await projects.SetIconImageAsync(externalId, request.Mime, blob, request.Source, ct);
		ProjectDto dto = ProjectDto.From(project);

		await this.PublishAsync(ProjectEventNames.Updated, new ProjectUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	[HttpDelete("{externalId}/icon")]
	[ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectDto>> RemoveIcon([FromRoute] string externalId, CancellationToken ct) {
		ProjectEntity project = await projects.RemoveIconAsync(externalId, ct);
		ProjectDto dto = ProjectDto.From(project);

		await this.PublishAsync(ProjectEventNames.Updated, new ProjectUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	// ── Chat move (lives here because it's a project-relationship op) ────

	[HttpPost("/api/chats/{chatExternalId}/move")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> MoveChat(
		[FromRoute] string chatExternalId,
		[FromBody]  MoveChatRequest request,
		CancellationToken ct
	) {
		// MoveChatToProjectAsync throws KeyNotFoundException if chat or project is missing → 404 via ApiExceptionFilter.
		await projects.MoveChatToProjectAsync(chatExternalId, request.ProjectExternalId, ct);
		await this.PublishAsync(
			ProjectEventNames.ChatMoved,
			new ChatMovedPayload(chatExternalId, request.ProjectExternalId),
			ct
		);
		return this.NoContent();
	}

	// ── Helpers ────────────────────────────────────────────────────

	private ValueTask PublishAsync(string name, object data, CancellationToken ct) =>
		events.PublishAsync(new ServerEvent(0, name, data, time.GetUtcNow()), ct);
}
