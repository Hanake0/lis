using System.Diagnostics;

using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Persistence.Entities;
using Lis.Core.Util;
using Lis.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ConversationService(
	IServiceScopeFactory scopeFactory,
	IChannelClient channelClient,
	Kernel kernel,
	ContextWindowBuilder contextWindowBuilder,
	PromptComposer promptComposer,
	ModelSettings modelSettings,
	IOptions<LisOptions> lisOptions,
	ILogger<ConversationService> logger) :IConversationService {

	[Trace("ConversationService > HandleIncomingAsync")]
	public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id", message.ChatId);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity chat = await UpsertChatAsync(db, message, ct);
		await PersistMessageAsync(db, chat, message, ct);

		if (!this.ShouldRespond(message)) {
			return;
		}

		await channelClient.SetTypingAsync(message.ChatId, ct);

		List<MessageEntity> recentMessages = await db.Messages
			.Where(m => m.ChatId == chat.Id)
			.OrderByDescending(m => m.Timestamp)
			.Take(lisOptions.Value.MaxRecentMessages)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, ct);

		ChatHistory chatHistory = contextWindowBuilder.Build(systemPrompt, recentMessages);

		IChatCompletionService chatService = kernel.GetRequiredService<IChatCompletionService>();
		ChatMessageContent response = await chatService.GetChatMessageContentAsync(
			chatHistory,
			new PromptExecutionSettings {
				ExtensionData = new Dictionary<string, object> {
					["max_tokens"] = modelSettings.MaxTokens,
				},
			},
			kernel,
			ct);

		string responseText = response.Content ?? "...";

		await PersistOutgoingMessageAsync(db, chat, responseText, ct);

		await channelClient.SendMessageAsync(message.ChatId, responseText, message.ExternalId, ct);

		try {
			await channelClient.MarkReadAsync(message.ExternalId, message.ChatId, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to mark message as read");
		}
	}

	private bool ShouldRespond(IncomingMessage message) {
		string ownerJid = lisOptions.Value.OwnerJid;

		if (string.IsNullOrEmpty(ownerJid)) {
			return !message.IsGroup;
		}

		if (message.IsGroup) {
			return false;
		}

		return message.SenderId == ownerJid;
	}

	private static async Task<ChatEntity> UpsertChatAsync(
		LisDbContext db, IncomingMessage message, CancellationToken ct) {
		ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == message.ChatId, ct);

		if (chat is null) {
			chat = new ChatEntity {
				ExternalId = message.ChatId,
				Name       = message.SenderName,
				IsGroup    = message.IsGroup,
				CreatedAt  = DateTimeOffset.UtcNow,
				UpdatedAt  = DateTimeOffset.UtcNow,
			};
			db.Chats.Add(chat);
			await db.SaveChangesAsync(ct);
		} else {
			chat.UpdatedAt = DateTimeOffset.UtcNow;
			if (message.SenderName is not null) {
				chat.Name = message.SenderName;
			}

			await db.SaveChangesAsync(ct);
		}

		return chat;
	}

	private static async Task PersistMessageAsync(
		LisDbContext db, ChatEntity chat, IncomingMessage message, CancellationToken ct) {
		MessageEntity entity = new() {
			ExternalId   = message.ExternalId,
			ChatId       = chat.Id,
			SenderId     = message.SenderId,
			SenderName   = message.SenderName,
			IsFromMe     = message.IsFromMe,
			Body         = message.Body,
			MediaType    = message.MediaType,
			MediaCaption = message.MediaCaption,
			ReplyToId    = message.RepliedId,
			TokenCount   = ContextWindowBuilder.EstimateTokens(message.Body),
			Timestamp    = message.Timestamp,
			CreatedAt    = DateTimeOffset.UtcNow,
		};

		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
	}

	private static async Task PersistOutgoingMessageAsync(		LisDbContext db, ChatEntity chat, string body, CancellationToken ct) {
		MessageEntity entity = new() {
			ChatId     = chat.Id,
			SenderId   = "me",
			IsFromMe   = true,
			Body       = body,
			TokenCount = ContextWindowBuilder.EstimateTokens(body),
			Timestamp  = DateTimeOffset.UtcNow,
			CreatedAt  = DateTimeOffset.UtcNow,
		};

		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
	}
}
