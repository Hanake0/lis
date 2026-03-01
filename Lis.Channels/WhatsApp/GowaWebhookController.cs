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
	WebhookValidator validator,
	IConversationService conversationService,
	ILogger<GowaWebhookController> logger) :ControllerBase {

	[HttpPost]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> HandleWebhook() {
		byte[] body = await ReadBodyAsync(this.Request);

		string signature = this.Request.Headers["X-Hub-Signature-256"].ToString();
		if (!string.IsNullOrEmpty(signature) && !validator.Validate(signature, body))
			return this.Unauthorized();

		WebhookPayload? payload;
		try {
			payload = JsonSerializer.Deserialize<WebhookPayload>(body);
		} catch (JsonException) {
			return this.BadRequest();
		}

		if (payload is null || string.IsNullOrEmpty(payload.Body) || payload.IsFromMe)
			return this.Ok();

		IncomingMessage message = new() {
			ExternalId   = payload.Id,
			ChatId       = payload.ChatJid,
			SenderId     = payload.SenderJid,
			SenderName   = payload.SenderName,
			Timestamp    = DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp),
			IsFromMe     = payload.IsFromMe,
			IsGroup      = payload.IsGroup,
			Body         = payload.Body,
			RepliedId    = payload.RepliedId,
			MediaType    = payload.MediaType,
			MediaCaption = payload.MediaCaption,
		};

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
