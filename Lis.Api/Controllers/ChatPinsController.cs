using Lis.Api.Schemas.Events;
using Lis.Core.Events;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Pin / unpin endpoints scoped to a chat. Lives in its own controller for now;
/// Stage 13's <c>ChatsController</c> can absorb these later if we want a single
/// chat-rooted surface.
/// </summary>
[ApiController]
[Route("api/chats")]
[Tags("ChatPins")]
[Produces("application/json")]
public sealed class ChatPinsController(
	IChatPinService pins,
	IEventBus       events,
	TimeProvider    time
) :ControllerBase {

	[HttpPost("{chatExternalId}/pin")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Pin([FromRoute] string chatExternalId, CancellationToken ct) {
		ChatEntity chat = await pins.PinAsync(chatExternalId, ct);
		await this.PublishAsync(
			ChatPinEventNames.Pinned,
			new ChatPinnedPayload(chat.ExternalId, chat.PinnedAt!.Value),
			ct
		);
		return this.NoContent();
	}

	[HttpDelete("{chatExternalId}/pin")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Unpin([FromRoute] string chatExternalId, CancellationToken ct) {
		await pins.UnpinAsync(chatExternalId, ct);
		await this.PublishAsync(
			ChatPinEventNames.Unpinned,
			new ChatUnpinnedPayload(chatExternalId),
			ct
		);
		return this.NoContent();
	}

	private ValueTask PublishAsync(string name, object data, CancellationToken ct) =>
		events.PublishAsync(new ServerEvent(0, name, data, time.GetUtcNow()), ct);
}
