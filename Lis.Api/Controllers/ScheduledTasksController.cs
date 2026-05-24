using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Scheduled;
using Lis.Api.Services;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// REST surface for <see cref="ScheduledTaskEntity"/>. CRUD operations notify the
/// <see cref="ScheduledTaskCoordinator"/> so the in-process scheduler re-syncs its
/// min-heap without ever polling the database.
/// </summary>
[ApiController]
[Tags("ScheduledTasks")]
[Produces("application/json")]
public sealed class ScheduledTasksController(
	IScheduledTaskService    service,
	LisDbContext             db,
	ScheduledTaskCoordinator coordinator,
	IEventBus                events,
	TimeProvider             time
) :ControllerBase {

	[HttpGet("/api/projects/{projectExternalId}/scheduled-tasks")]
	[ProducesResponseType(typeof(ScheduledTaskDto[]), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ScheduledTaskDto[]>> ListForProject(
		[FromRoute] string projectExternalId,
		CancellationToken  ct
	) {
		IReadOnlyList<ScheduledTaskEntity> tasks = await service.ListForProjectAsync(projectExternalId, ct);
		ScheduledTaskDto[] dtos = await this.ToDtoArrayAsync(tasks, ct);
		return this.Ok(dtos);
	}

	[HttpGet("/api/scheduled-tasks/{externalId}")]
	[ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ScheduledTaskDto>> Get([FromRoute] string externalId, CancellationToken ct) {
		ScheduledTaskEntity? task = await service.GetByExternalIdAsync(externalId, ct);
		if (task is null) return this.NotFound($"Scheduled task '{externalId}' not found");

		ScheduledTaskDto dto = ScheduledTaskDto.From(task, await this.ResolveProjectExternalIdAsync(task.ProjectId, ct));
		return this.Ok(dto);
	}

	[HttpPost("/api/scheduled-tasks")]
	[ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ScheduledTaskDto>> Create([FromBody] CreateScheduledTaskRequest request, CancellationToken ct) {
		ScheduledTaskEntity task = await service.CreateAsync(new CreateScheduledTaskInput(
			Name:              request.Name,
			CronExpression:    request.CronExpression,
			Prompt:            request.Prompt,
			ProjectExternalId: request.ProjectId,
			AgentName:         request.AgentName,
			Model:             request.Model,
			Timezone:          request.Timezone,
			Enabled:           request.Enabled
		), ct);

		ScheduledTaskDto dto = ScheduledTaskDto.From(task, request.ProjectId);
		await coordinator.NotifyChangedAsync(task.Id, ct);
		await this.PublishAsync(ScheduledTaskEventNames.Created, new ScheduledTaskCreatedPayload(dto), ct);

		return this.CreatedAtAction(nameof(this.Get), new { externalId = dto.Id }, dto);
	}

	[HttpPatch("/api/scheduled-tasks/{externalId}")]
	[ProducesResponseType(typeof(ScheduledTaskDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ScheduledTaskDto>> Update(
		[FromRoute] string                       externalId,
		[FromBody]  UpdateScheduledTaskRequest   request,
		CancellationToken                        ct
	) {
		ScheduledTaskEntity task = await service.UpdateAsync(externalId, new UpdateScheduledTaskInput(
			Name:              request.Name,
			CronExpression:    request.CronExpression,
			Prompt:            request.Prompt,
			ProjectExternalId: request.ProjectId,
			AgentName:         request.AgentName,
			Model:             request.Model,
			Timezone:          request.Timezone,
			Enabled:           request.Enabled
		), ct);

		string? projectExt = await this.ResolveProjectExternalIdAsync(task.ProjectId, ct);
		ScheduledTaskDto dto = ScheduledTaskDto.From(task, projectExt);

		await coordinator.NotifyChangedAsync(task.Id, ct);
		await this.PublishAsync(ScheduledTaskEventNames.Updated, new ScheduledTaskUpdatedPayload(dto), ct);

		return this.Ok(dto);
	}

	[HttpDelete("/api/scheduled-tasks/{externalId}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Delete([FromRoute] string externalId, CancellationToken ct) {
		ScheduledTaskEntity? task = await service.GetByExternalIdAsync(externalId, ct);
		if (task is null) return this.NotFound($"Scheduled task '{externalId}' not found");

		long id = task.Id;
		await service.DeleteAsync(externalId, ct);
		coordinator.NotifyRemoved(id);
		await this.PublishAsync(ScheduledTaskEventNames.Deleted, new ScheduledTaskDeletedPayload(externalId), ct);

		return this.NoContent();
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task<ScheduledTaskDto[]> ToDtoArrayAsync(IReadOnlyList<ScheduledTaskEntity> tasks, CancellationToken ct) {
		if (tasks.Count == 0) return [];

		// Batch the project lookup so the controller does at most one DB roundtrip per page.
		HashSet<long> projectIds = tasks.Where(t => t.ProjectId is not null).Select(t => t.ProjectId!.Value).ToHashSet();
		Dictionary<long, string> projects = projectIds.Count == 0
			? []
			: await db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ExternalId, ct);

		return tasks.Select(t => ScheduledTaskDto.From(
			t,
			t.ProjectId is not null && projects.TryGetValue(t.ProjectId.Value, out string? ext) ? ext : null
		)).ToArray();
	}

	private async Task<string?> ResolveProjectExternalIdAsync(long? projectId, CancellationToken ct) {
		if (projectId is null) return null;
		return await db.Projects.Where(p => p.Id == projectId.Value).Select(p => p.ExternalId).FirstOrDefaultAsync(ct);
	}

	private ValueTask PublishAsync(string name, object data, CancellationToken ct) =>
		events.PublishAsync(new ServerEvent(0, name, data, time.GetUtcNow()), ct);
}
