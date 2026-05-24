using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Folders;
using Lis.Core.Events;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lis.Api.Controllers;

/// <summary>
/// REST surface for <see cref="SessionFolderEntity"/>. Mutations emit SSE events so other
/// connected clients refresh their sidebars without polling.
/// </summary>
[ApiController]
[Route("api/session-folders")]
[Tags("SessionFolders")]
[Produces("application/json")]
public sealed class SessionFoldersController(
	ISessionFolderService folders,
	IEventBus             events,
	TimeProvider          time
) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(SessionFolderDto[]), StatusCodes.Status200OK)]
	public async Task<ActionResult<SessionFolderDto[]>> List(
		[FromQuery(Name = "scope"), BindRequired] string scope,
		CancellationToken                                ct
	) {
		IReadOnlyList<SessionFolderEntity> all = await folders.ListForScopeAsync(scope, ct);
		return this.Ok(all.Select(SessionFolderDto.From).ToArray());
	}

	[HttpGet("{externalId}")]
	[ProducesResponseType(typeof(SessionFolderDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<SessionFolderDto>> Get([FromRoute] string externalId, CancellationToken ct) {
		SessionFolderEntity? folder = await folders.GetByExternalIdAsync(externalId, ct);
		return folder is null
			? this.NotFound($"Folder '{externalId}' not found")
			: this.Ok(SessionFolderDto.From(folder));
	}

	[HttpPost]
	[ProducesResponseType(typeof(SessionFolderDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<SessionFolderDto>> Create([FromBody] CreateSessionFolderRequest request, CancellationToken ct) {
		SessionFolderEntity folder = await folders.CreateAsync(request.ScopeKey, request.Name, request.ParentId, ct);
		SessionFolderDto dto = SessionFolderDto.From(folder);

		await this.PublishAsync(FolderEventNames.Created, new FolderCreatedPayload(dto), ct);
		return this.CreatedAtAction(nameof(this.Get), new { externalId = dto.Id }, dto);
	}

	[HttpPatch("{externalId}")]
	[ProducesResponseType(typeof(SessionFolderDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<SessionFolderDto>> Rename(
		[FromRoute] string                      externalId,
		[FromBody]  RenameSessionFolderRequest  request,
		CancellationToken                       ct
	) {
		SessionFolderEntity folder = await folders.RenameAsync(externalId, request.Name, ct);
		SessionFolderDto dto = SessionFolderDto.From(folder);

		await this.PublishAsync(FolderEventNames.Updated, new FolderUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	[HttpDelete("{externalId}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Delete([FromRoute] string externalId, CancellationToken ct) {
		await folders.DeleteAsync(externalId, ct);
		await this.PublishAsync(FolderEventNames.Deleted, new FolderDeletedPayload(externalId), ct);
		return this.NoContent();
	}

	[HttpPost("reorder")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> Reorder([FromBody] ReorderSessionFoldersRequest request, CancellationToken ct) {
		await folders.ReorderAsync(request.ScopeKey, request.OrderedExternalIds, ct);
		IReadOnlyList<SessionFolderEntity> after = await folders.ListForScopeAsync(request.ScopeKey, ct);
		foreach (string id in request.OrderedExternalIds) {
			SessionFolderEntity? f = after.FirstOrDefault(x => x.ExternalId == id);
			if (f is null) continue;
			await this.PublishAsync(FolderEventNames.Updated, new FolderUpdatedPayload(SessionFolderDto.From(f)), ct);
		}
		return this.NoContent();
	}

	[HttpPost("{externalId}/items")]
	[ProducesResponseType(typeof(SessionFolderDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<SessionFolderDto>> AddItems(
		[FromRoute] string                externalId,
		[FromBody]  AddSessionsRequest    request,
		CancellationToken                 ct
	) {
		await folders.AddSessionsAsync(externalId, request.ChatIds, ct);
		SessionFolderEntity folder = (await folders.GetByExternalIdAsync(externalId, ct))!;
		SessionFolderDto dto = SessionFolderDto.From(folder);

		await this.PublishAsync(FolderEventNames.Updated, new FolderUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	[HttpDelete("{externalId}/items/{chatId}")]
	[ProducesResponseType(typeof(SessionFolderDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<SessionFolderDto>> RemoveItem(
		[FromRoute] string externalId,
		[FromRoute] string chatId,
		CancellationToken  ct
	) {
		await folders.RemoveSessionAsync(externalId, chatId, ct);
		SessionFolderEntity folder = (await folders.GetByExternalIdAsync(externalId, ct))!;
		SessionFolderDto dto = SessionFolderDto.From(folder);

		await this.PublishAsync(FolderEventNames.Updated, new FolderUpdatedPayload(dto), ct);
		return this.Ok(dto);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private ValueTask PublishAsync(string name, object data, CancellationToken ct) =>
		events.PublishAsync(new ServerEvent(0, name, data, time.GetUtcNow()), ct);
}
