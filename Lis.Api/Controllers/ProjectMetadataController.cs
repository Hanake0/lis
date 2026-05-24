using Lis.Api.Schemas.Projects;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// Per-project key/value metadata: notes, todos, worktree setup commands, plan-file
/// content, etc. The sidebar dialogs (ProjectNotesTodoPanel, NewWorktreeDialog) read
/// + write these via the openchamberConfig.ts shim. One row per (project, key) so
/// each update is independent.
///
/// Auth: covered by the global `Authorization: Bearer ${WEB_CHANNEL_TOKEN}` middleware.
/// CORS: same group as the other `/api/projects/*` endpoints.
/// </summary>
[ApiController]
[Route("api/projects/{projectExternalId}/metadata")]
[Tags("Projects")]
[Produces("application/json")]
public sealed class ProjectMetadataController(LisDbContext db, TimeProvider time) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(ProjectMetadataEntryDto[]), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),            StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectMetadataEntryDto[]>> List(
		[FromRoute] string projectExternalId,
		CancellationToken   ct
	) {
		long projectId = await this.ResolveProjectIdAsync(projectExternalId, ct);

		ProjectMetadataEntryDto[] entries = await db.ProjectMetadata
			.Where(m => m.ProjectId == projectId)
			.OrderBy(m => m.Key)
			.Select(m => new ProjectMetadataEntryDto(m.Key, m.Value, m.UpdatedAt))
			.ToArrayAsync(ct);
		return this.Ok(entries);
	}

	[HttpGet("{key}")]
	[ProducesResponseType(typeof(ProjectMetadataValueDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectMetadataValueDto>> Get(
		[FromRoute] string projectExternalId,
		[FromRoute] string key,
		CancellationToken   ct
	) {
		long projectId = await this.ResolveProjectIdAsync(projectExternalId, ct);

		ProjectMetadataEntity? row = await db.ProjectMetadata
			.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Key == key, ct);

		return row is null
			? this.Ok(new ProjectMetadataValueDto(key, null,     false, null))
			: this.Ok(new ProjectMetadataValueDto(key, row.Value, true,  row.UpdatedAt));
	}

	[HttpPut("{key}")]
	[ProducesResponseType(typeof(ProjectMetadataEntryDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ProjectMetadataEntryDto>> Set(
		[FromRoute] string                     projectExternalId,
		[FromRoute] string                     key,
		[FromBody]  SetProjectMetadataRequest  request,
		CancellationToken                      ct
	) {
		if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("metadata key is required", nameof(key));
		if (request is null)                throw new ArgumentException("request body is required", nameof(request));

		long projectId = await this.ResolveProjectIdAsync(projectExternalId, ct);
		DateTimeOffset now = time.GetUtcNow();

		ProjectMetadataEntity? row = await db.ProjectMetadata
			.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Key == key, ct);

		if (row is null) {
			row = new ProjectMetadataEntity {
				ProjectId = projectId,
				Key       = key,
				Value     = request.Value,
				UpdatedAt = now,
			};
			db.ProjectMetadata.Add(row);
		} else {
			row.Value     = request.Value;
			row.UpdatedAt = now;
		}

		await db.SaveChangesAsync(ct);
		return this.Ok(new ProjectMetadataEntryDto(row.Key, row.Value, row.UpdatedAt));
	}

	[HttpDelete("{key}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Delete(
		[FromRoute] string projectExternalId,
		[FromRoute] string key,
		CancellationToken   ct
	) {
		long projectId = await this.ResolveProjectIdAsync(projectExternalId, ct);

		ProjectMetadataEntity? row = await db.ProjectMetadata
			.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Key == key, ct);
		if (row is not null) {
			db.ProjectMetadata.Remove(row);
			await db.SaveChangesAsync(ct);
		}

		return this.NoContent();
	}

	private async Task<long> ResolveProjectIdAsync(string externalId, CancellationToken ct) {
		long? projectId = await db.Projects
			.Where(p => p.ExternalId == externalId)
			.Select(p => (long?)p.Id)
			.FirstOrDefaultAsync(ct);
		return projectId
			?? throw new KeyNotFoundException($"Project '{externalId}' not found");
	}
}
