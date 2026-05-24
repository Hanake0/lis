using Lis.Agent;
using Lis.Api.Schemas.Chats;
using Lis.Api.Schemas.Events;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// Chats REST surface. Stage 13 is the full migration of every WebChannel
/// chat operation onto this controller; today the create endpoint is wired
/// because the multi-run orchestrator (Stage 7 backend, Stage 10 frontend)
/// needs to mint chats from the webview without going through WebChannel.
///
/// Re-using an existing chat id (e.g. a deterministic id the client minted
/// before realising it already POSTed) is treated as idempotent — the
/// existing row is returned with 200 OK and no SSE event is re-emitted.
/// </summary>
[ApiController]
[Route("api/chats")]
[Tags("Chats")]
[Produces("application/json")]
public sealed class ChatsController(
	LisDbContext            db,
	IEventBus               events,
	TimeProvider            time,
	IChatGenerationService  generation
) :ControllerBase {

	/// <summary>
	/// Lists every chat the server knows about, newest-first by <c>updated_at</c>.
	/// Includes archived rows — the caller (sidebar) buckets them by <c>archived_at</c>.
	/// Project relation is eager-loaded so the DTO's <c>project_id</c> is the project's
	/// external id, not the FK.
	/// </summary>
	[HttpGet]
	[ProducesResponseType(typeof(ChatDto[]), StatusCodes.Status200OK)]
	public async Task<ActionResult<ChatDto[]>> List(CancellationToken ct) {
		ChatDto[] chats = await db.Chats
			.Include(c => c.Project)
			.OrderByDescending(c => c.UpdatedAt)
			.Select(c => ChatDto.From(c))
			.ToArrayAsync(ct);
		return this.Ok(chats);
	}

	[HttpPost]
	[ProducesResponseType(typeof(ChatDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ChatDto>> Create([FromBody] CreateChatRequest request, CancellationToken ct) {
		string chatId = string.IsNullOrWhiteSpace(request.ChatId)
			? $"web:{Guid.NewGuid():N}"
			: request.ChatId.Trim();

		// Idempotent on caller-supplied id — repeat POSTs return the existing row.
		ChatEntity? existing = await db.Chats
			.Include(c => c.Project)
			.FirstOrDefaultAsync(c => c.ExternalId == chatId, ct);
		if (existing is not null) return this.Ok(ChatDto.From(existing));

		long? projectFkId = null;
		ProjectEntity? boundProject = null;
		if (!string.IsNullOrWhiteSpace(request.ProjectExternalId)) {
			boundProject = await db.Projects.FirstOrDefaultAsync(p => p.ExternalId == request.ProjectExternalId, ct)
				?? throw new KeyNotFoundException($"Project '{request.ProjectExternalId}' not found");
			projectFkId = boundProject.Id;
		}

		DateTimeOffset now = time.GetUtcNow();
		ChatEntity chat = new() {
			ExternalId = chatId,
			Name       = string.IsNullOrWhiteSpace(request.Name) ? "New Chat" : request.Name.Trim(),
			Channel    = string.IsNullOrWhiteSpace(request.Channel) ? "web" : request.Channel.Trim(),
			ProjectId  = projectFkId,
			Project    = boundProject,
			CreatedAt  = now,
			UpdatedAt  = now,
			Enabled    = true,
		};
		db.Chats.Add(chat);
		await db.SaveChangesAsync(ct);

		ChatDto dto = ChatDto.From(chat);
		await events.PublishAsync(new ServerEvent(0, ChatEventNames.Created, new ChatCreatedPayload(dto), now), ct);

		return this.CreatedAtAction(nameof(this.Create), new { chatId = dto.ChatId }, dto);
	}

	/// <summary>
	/// Rename a chat. Only <c>name</c> is patchable here — broader chat editing
	/// (project move, agent reassignment, override save) goes through dedicated
	/// endpoints with their own permission/validation rules.
	/// </summary>
	[HttpPatch("{chatExternalId}")]
	[ProducesResponseType(typeof(ChatDto),         StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),  StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails),  StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ChatDto>> Update(
		[FromRoute] string             chatExternalId,
		[FromBody]  UpdateChatRequest  request,
		CancellationToken              ct
	) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));

		ChatEntity chat = await this.FindByExternalIdAsync(chatExternalId, includeProject: true, ct);

		if (request.Name is not null) {
			string trimmed = request.Name.Trim();
			if (trimmed.Length == 0) throw new ArgumentException("name cannot be empty", nameof(request));
			chat.Name = trimmed;
		}
		chat.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);

		ChatDto dto = ChatDto.From(chat);
		await events.PublishAsync(new ServerEvent(0, ChatEventNames.Updated, new ChatUpdatedPayload(dto), chat.UpdatedAt), ct);
		return this.Ok(dto);
	}

	/// <summary>
	/// Archive a chat — sets <c>archived_at</c> without deleting any data. The
	/// chat keeps its messages and stays addressable; the sidebar buckets it
	/// under "Archived". Re-archiving an already-archived chat is a no-op.
	/// </summary>
	[HttpPost("{chatExternalId}/archive")]
	[ProducesResponseType(typeof(ChatDto),        StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<ActionResult<ChatDto>> Archive(
		[FromRoute] string chatExternalId,
		CancellationToken  ct
	) {
		ChatEntity chat = await this.FindByExternalIdAsync(chatExternalId, includeProject: true, ct);

		DateTimeOffset now = time.GetUtcNow();
		// Idempotent: only stamp + emit when we actually flip the bucket.
		if (chat.ArchivedAt is null) {
			chat.ArchivedAt = now;
			chat.UpdatedAt  = now;
			await db.SaveChangesAsync(ct);
			ChatDto dto = ChatDto.From(chat);
			await events.PublishAsync(new ServerEvent(0, ChatEventNames.Archived, new ChatArchivedPayload(dto), now), ct);
			return this.Ok(dto);
		}
		return this.Ok(ChatDto.From(chat));
	}

	/// <summary>
	/// Delete a chat row and every message under it. Idempotent — deleting a
	/// missing chat returns 204 so racing clients don't trip on each other.
	/// </summary>
	[HttpDelete("{chatExternalId}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> Delete(
		[FromRoute] string chatExternalId,
		CancellationToken  ct
	) {
		ChatEntity? chat = await db.Chats.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId, ct);
		if (chat is null) {
			// Idempotent — still publish so any racing client clears its local row.
			await events.PublishAsync(new ServerEvent(0, ChatEventNames.Deleted, new ChatDeletedPayload(chatExternalId), time.GetUtcNow()), ct);
			return this.NoContent();
		}

		db.Chats.Remove(chat);
		await db.SaveChangesAsync(ct);
		await events.PublishAsync(new ServerEvent(0, ChatEventNames.Deleted, new ChatDeletedPayload(chatExternalId), time.GetUtcNow()), ct);
		return this.NoContent();
	}

	[HttpPost("{chatExternalId}/generate")]
	[ProducesResponseType(typeof(GenerateResponseDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),      StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails),      StatusCodes.Status404NotFound)]
	public async Task<ActionResult<GenerateResponseDto>> Generate(
		[FromRoute] string          chatExternalId,
		[FromBody]  GenerateRequest request,
		CancellationToken           ct
	) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));

		ChatGenerationService.GenerationResult result = await generation.GenerateAsync(
			new ChatGenerationService.GenerationRequest(
				ChatExternalId: chatExternalId,
				VisiblePrompt:  request.VisiblePrompt,
				HiddenPrompt:   request.HiddenPrompt,
				Kind:           string.IsNullOrWhiteSpace(request.Kind) ? "generate" : request.Kind.Trim(),
				ModelOverride:  request.Model
			),
			ct
		);

		return this.Ok(new GenerateResponseDto(result.Text, result.InputTokens, result.OutputTokens));
	}

	// ── helpers ────────────────────────────────────────────────────

	private async Task<ChatEntity> FindByExternalIdAsync(string externalId, bool includeProject, CancellationToken ct) {
		IQueryable<ChatEntity> query = db.Chats;
		if (includeProject) query = query.Include(c => c.Project);
		ChatEntity? chat = await query.FirstOrDefaultAsync(c => c.ExternalId == externalId, ct);
		return chat ?? throw new KeyNotFoundException($"Chat '{externalId}' not found");
	}
}
