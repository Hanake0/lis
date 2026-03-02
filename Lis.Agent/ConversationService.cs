using System.Diagnostics;
using System.Text.Json;

using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ConversationService(
	IServiceScopeFactory         scopeFactory,
	IChannelClient               channelClient,
	Kernel                       kernel,
	ToolRunner                   toolRunner,
	ContextWindowBuilder         contextWindowBuilder,
	PromptComposer               promptComposer,
	ModelSettings                modelSettings,
	IOptions<LisOptions>         lisOptions,
	ILogger<ConversationService> logger) : IConversationService {
	[Trace("ConversationService > HandleIncomingAsync")]
	public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
		// Skip echoes of our own messages (tool notifications, AI responses).
		// All AI messages are already persisted by PersistSkMessageAsync with sk_content.
		if (message.IsFromMe) return;

		(_, bool shouldRespond) = await this.IngestMessageAsync(message, ct);
		if (shouldRespond)
			await this.RespondAsync(message, ct);
	}

	public Task HandleTypingAsync(string chatId, CancellationToken ct) => Task.CompletedTask;

	[Trace("ConversationService > IngestMessageAsync")]
	public async Task<(ChatEntity Chat, bool ShouldRespond)> IngestMessageAsync(
		IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity chat = await UpsertChatAsync(db, message, ct);
		await PersistMessageAsync(db, chat, message, ct);

		try {
			await channelClient.MarkReadAsync(message.ExternalId, message.ChatId, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to mark message as read");
		}

		return (chat, this.ShouldRespond(message));
	}

	[Trace("ConversationService > RespondAsync")]
	public async Task RespondAsync(IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		await channelClient.SetTypingAsync(message.ChatId, ct);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity? chat = await db.Chats
								   .FirstOrDefaultAsync(c => c.ExternalId == message.ChatId, ct);

		if (chat is null) {
			logger.LogWarning("Chat not found for {ChatId} during respond phase", message.ChatId);
			return;
		}

		List<MessageEntity> recentMessages = await db.Messages
													 .Where(m => m.ChatId == chat.Id)
													 .OrderByDescending(m => m.Timestamp)
													 .Take(lisOptions.Value.MaxRecentMessages)
													 .OrderBy(m => m.Timestamp)
													 .ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, ct);

		ChatHistory chatHistory = contextWindowBuilder.Build(systemPrompt, recentMessages);

		ToolContext.ChatId  = message.ChatId;
		ToolContext.Channel = channelClient;

		IChatCompletionService chatService = kernel.GetRequiredService<IChatCompletionService>();
		PromptExecutionSettings settings = new() {
			ModelId                = modelSettings.Model,
			FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
			ExtensionData          = new Dictionary<string, object> { ["max_tokens"] = modelSettings.MaxTokens }
		};

		await foreach (ChatMessageContent msg in toolRunner.RunAsync(chatService, chatHistory, kernel, settings, ct)) {
			if (msg.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(msg.Content))
				await channelClient.SendMessageAsync(message.ChatId, msg.Content, message.ExternalId, ct);

			await PersistSkMessageAsync(db, chat, msg, ct);
		}
	}

	private bool ShouldRespond(IncomingMessage message) {
		string ownerJid = lisOptions.Value.OwnerJid;

		if (string.IsNullOrEmpty(ownerJid)) return !message.IsGroup;

		if (message.IsGroup) return false;

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
				UpdatedAt  = DateTimeOffset.UtcNow
			};
			db.Chats.Add(chat);
			await db.SaveChangesAsync(ct);
		} else {
			chat.UpdatedAt = DateTimeOffset.UtcNow;
			if (message.SenderName is not null) chat.Name = message.SenderName;

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
			CreatedAt    = DateTimeOffset.UtcNow
		};

		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
	}

	private static async Task PersistSkMessageAsync(
		LisDbContext db, ChatEntity chat, ChatMessageContent msg, CancellationToken ct) {
		db.Messages.Add(new MessageEntity {
			ChatId     = chat.Id,
			SenderId   = "me",
			IsFromMe   = msg.Role != AuthorRole.User,
			Body       = msg.Content,
			SkContent  = JsonSerializer.Serialize(msg),
			TokenCount = ContextWindowBuilder.EstimateTokens(msg.Content),
			Timestamp  = DateTimeOffset.UtcNow,
			CreatedAt  = DateTimeOffset.UtcNow
		});
		await db.SaveChangesAsync(ct);
	}
}
