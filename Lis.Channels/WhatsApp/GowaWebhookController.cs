using System.Text.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Channel;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lis.Channels.WhatsApp;

[ApiController]
[Route("webhook/whatsapp")]
[Tags("WhatsApp")]
public class GowaWebhookController(
	WebhookValidator               validator,
	IConversationService           conversationService,
	ILogger<GowaWebhookController> logger) : ControllerBase {
	[HttpPost]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> HandleWebhook() {
		byte[] body = await ReadBodyAsync(this.Request);

		string signature = this.Request.Headers["X-Hub-Signature-256"].ToString();
		if (!string.IsNullOrEmpty(signature) && !validator.Validate(signature, body))
			return this.Unauthorized();

		WebhookEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<WebhookEnvelope>(body);
		} catch (JsonException) {
			return this.BadRequest();
		}

		WebhookPayload? payload = envelope?.Payload;

		// Typing/composing events → extend debounce timer (requires GOWA PR #547)
		if (envelope?.Event is "chat_presence" && !string.IsNullOrEmpty(payload?.ChatId)) {
			_ = Task.Run(() => conversationService.HandleTypingAsync(payload.ChatId, CancellationToken.None));
			return this.Ok();
		}

		if (payload is null || string.IsNullOrEmpty(payload.Id))
			return this.Ok();

		// Reaction events: GOWA sends a "reaction" extension field with {"emoji":"...", "message_id":"..."}
		if (payload.Extensions?.TryGetValue("reaction", out JsonElement reactionEl) == true
		    && reactionEl.ValueKind == JsonValueKind.Object) {

			string? emoji     = reactionEl.TryGetProperty("emoji", out JsonElement emojiProp) ? emojiProp.GetString() : null;
			string? reactedId = reactionEl.TryGetProperty("message_id", out JsonElement msgIdProp) ? msgIdProp.GetString() : null;

			if (!string.IsNullOrEmpty(emoji) && !string.IsNullOrEmpty(reactedId) && !string.IsNullOrEmpty(payload.ChatId)) {
				string senderId = payload.From ?? "";
				_ = Task.Run(async () => {
					try {
						await conversationService.HandleReactionAsync(reactedId, payload.ChatId, emoji, senderId, CancellationToken.None);
					} catch (Exception ex) {
						logger.LogError(ex, "Error processing reaction on {MessageId}", reactedId);
					}
				});
			}

			return this.Ok();
		}

		// Group JIDs end with @g.us
		bool isGroup = payload.ChatId?.EndsWith("@g.us") is true;

		DateTimeOffset timestamp = DateTimeOffset.TryParse(payload.Timestamp, out DateTimeOffset ts)
			? ts
			: DateTimeOffset.UtcNow;

		IncomingMessage message = new() {
			ExternalId     = payload.Id,
			ChatId         = payload.ChatId ?? "",
			SenderId       = payload.From   ?? "",
			SenderName     = payload.FromName,
			Timestamp      = timestamp,
			IsFromMe       = payload.IsFromMe,
			IsGroup        = isGroup,
			Body           = payload.Body,
			RepliedId      = payload.RepliedToId,
			RepliedContent = payload.QuotedBody,
			MediaType      = payload.MediaType,
			MediaCaption   = payload.MediaCaption,
			MediaPath      = payload.MediaPath
		};

		// Echoes of our own messages → backfill sender info on the persisted record
		if (payload.IsFromMe) {
			_ = Task.Run(async () => {
				try {
					await conversationService.HandleSentEchoAsync(message, CancellationToken.None);
				} catch (Exception ex) {
					logger.LogError(ex, "Error processing echo {MessageId}", payload.Id);
				}
			});
			return this.Ok();
		}

		if (string.IsNullOrEmpty(payload.Body) && payload.MediaType is null)
			return this.Ok();

		_ = Task.Run(async () => {
			try {
				await conversationService.HandleIncomingAsync(message, CancellationToken.None);
			} catch (Exception ex) {
				logger.LogError(ex, "Error processing message {MessageId}", payload.Id);
			}
		});

		return this.Ok();
	}

	private static async Task<byte[]> ReadBodyAsync(HttpRequest request) {
		using MemoryStream ms = new();
		await request.Body.CopyToAsync(ms);
		return ms.ToArray();
	}
}
