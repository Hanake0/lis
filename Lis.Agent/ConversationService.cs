using System.Diagnostics;
using System.Text.Json;

using Lis.Agent.Commands;
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
	CompactionService            compactionService,
	CommandRouter                commandRouter,
	ModelSettings                modelSettings,
	IOptions<LisOptions>         lisOptions,
	ILogger<ConversationService> logger,
	ITokenCounter?               tokenCounter = null) : IConversationService {

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

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity? chat = await db.Chats
			.Include(c => c.CurrentSession)
			.FirstOrDefaultAsync(c => c.ExternalId == message.ChatId, ct);

		if (chat is null) {
			logger.LogWarning("Chat not found for {ChatId} during respond phase", message.ChatId);
			return;
		}

		// Ensure session exists
		SessionEntity session = await this.EnsureSessionAsync(db, chat, message.DbId, ct);

		// Handle commands before AI processing
		if (commandRouter.Match(message.Body) is { } match) {
			CommandContext ctx = new(message, chat, session, db, match.Args);
			string response = await match.Command.ExecuteAsync(ctx, ct);
			await channelClient.SendMessageAsync(message.ChatId, response, message.ExternalId, ct);

			// Persist so AI sees the response in history
			db.Messages.Add(new MessageEntity {
				ChatId    = chat.Id,
				SenderId  = "me",
				IsFromMe  = true,
				Role      = "assistant",
				Body      = response,
				Timestamp = DateTimeOffset.UtcNow,
				CreatedAt = DateTimeOffset.UtcNow
			});
			await db.SaveChangesAsync(ct);
			return;
		}

		await channelClient.SetTypingAsync(message.ChatId, ct);

		// Load messages from current session
		List<MessageEntity> recentMessages = await db.Messages
			.Where(m => m.ChatId == chat.Id
			         && (session.StartMessageId == null || m.Id >= session.StartMessageId))
			.OrderByDescending(m => m.Timestamp)
			.Take(lisOptions.Value.MaxRecentMessages)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, ct);

		// Load parent session for continuity
		SessionEntity? parentSession = session.ParentSessionId is not null
			? await db.Sessions.FindAsync([session.ParentSessionId], ct)
			: null;

		ChatHistory chatHistory = contextWindowBuilder.Build(
			systemPrompt, recentMessages, session, parentSession, lisOptions.Value);

		// Pre-send validation: count tokens when context is likely large
		if (tokenCounter is not null && session.ContextTokens > modelSettings.ContextBudget * 0.7) {
			try {
				string countJson = ChatHistorySerializer.ToAnthropicJson(chatHistory, modelSettings.Model);
				int? tokenCount = await tokenCounter.CountAsync(countJson, ct);
				if (tokenCount > modelSettings.ContextBudget)
					logger.LogWarning("Pre-send token count ({Tokens}) exceeds budget ({Budget})",
						tokenCount, modelSettings.ContextBudget);
			} catch (Exception ex) {
				logger.LogWarning(ex, "Pre-send token counting failed");
			}
		}

		ToolContext.ChatId               = message.ChatId;
		ToolContext.Channel              = channelClient;
		ToolContext.NotificationsEnabled = lisOptions.Value.ToolNotifications;

		IChatCompletionService chatService = kernel.GetRequiredService<IChatCompletionService>();

		Dictionary<string, object> extensionData = new() { ["max_tokens"] = modelSettings.MaxTokens };
		if (modelSettings.ThinkingEffort is { Length: > 0 } effort)
			extensionData["thinking"] = new Dictionary<string, object> {
				["type"] = "enabled",
				["budget_tokens"] = effort switch {
					"low"    => 1024,
					"medium" => 4096,
					"high"   => 16384,
					_ => int.TryParse(effort, out int t) ? t : 4096
				}
			};

		PromptExecutionSettings settings = new() {
			ModelId                = modelSettings.Model,
			FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
			ExtensionData          = extensionData
		};

		TokenUsage? lastUsage = null;

		await foreach (ChatMessageContent msg in toolRunner.RunAsync(chatService, chatHistory, kernel, settings, ct)) {
			if (msg.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(msg.Content))
				await channelClient.SendMessageAsync(message.ChatId, msg.Content, message.ExternalId, ct);

			// Usage is attached per-message by ToolRunner (only on assistant messages)
			TokenUsage? msgUsage = ToolRunner.GetUsage(msg);
			if (msgUsage is not null) lastUsage = msgUsage;
			await PersistSkMessageAsync(db, chat, msg, msgUsage, ct);
		}

		// Update session token stats from last response
		if (lastUsage is not null) {
			// Reload session — compaction may have completed during the AI loop
			await db.Entry(session).ReloadAsync(ct);

			// If session was finalized by compaction, skip updates (new session is current)
			if (session.EndMessageId is not null) return;

			session.TotalInputTokens         += lastUsage.InputTokens;
			session.TotalOutputTokens        += lastUsage.OutputTokens;
			session.TotalCacheReadTokens     += lastUsage.CacheReadTokens;
			session.TotalCacheCreationTokens += lastUsage.CacheCreationTokens;
			session.TotalThinkingTokens      += lastUsage.ThinkingTokens;
			session.ContextTokens             = lastUsage.TotalInputTokens;
			session.UpdatedAt                 = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync(ct);

			await this.CheckCompactionTriggersAsync(db, session, lastUsage, message.ChatId, ct);
		}
	}

	private async Task CheckCompactionTriggersAsync(
		LisDbContext db, SessionEntity session, TokenUsage usage, string chatId, CancellationToken ct) {
		int totalInput = usage.TotalInputTokens;

		// Full compaction takes priority — calculate split point from recent messages
		if (totalInput > lisOptions.Value.CompactionThreshold && !session.IsCompacting) {
			// Safeguard: also set tool prune boundary so keep_all yields to auto
			if (session.ToolsPrunedThroughId is null) {
				long? lastMsgId = await db.Messages
					.Where(m => m.ChatId == session.ChatId)
					.OrderByDescending(m => m.Id)
					.Select(m => (long?)m.Id)
					.FirstOrDefaultAsync(ct);
				session.ToolsPrunedThroughId = lastMsgId;
				await db.SaveChangesAsync(ct);
			}

			// Find split: walk backwards from newest, keep KeepRecentTokens
			List<MessageEntity> allMsgs = await db.Messages
				.Where(m => m.ChatId == session.ChatId
				         && (session.StartMessageId == null || m.Id >= session.StartMessageId))
				.OrderByDescending(m => m.Id)
				.ToListAsync(ct);

			long splitId = CompactionService.CalculateSplitPoint(allMsgs, lisOptions.Value.KeepRecentTokens);

			if (splitId > 0)
				_ = Task.Run(() => compactionService.CompactAsync(chatId, splitId, CancellationToken.None), CancellationToken.None);
			return;
		}

		// Tool pruning — count only tool result message tokens
		if (session.ToolsPrunedThroughId is null) {
			int toolTokens = await db.Messages
				.Where(m => m.ChatId == session.ChatId
				         && (session.StartMessageId == null || m.Id >= session.StartMessageId)
				         && m.Role == "tool")
				.SumAsync(m => m.OutputTokens ?? 0, ct);

			if (toolTokens > lisOptions.Value.ToolPruneThreshold) {
				int toolCount = await db.Messages
					.Where(m => m.ChatId == session.ChatId
					         && (session.StartMessageId == null || m.Id >= session.StartMessageId)
					         && m.Role == "tool")
					.CountAsync(ct);

				long? lastMsgId = await db.Messages
					.Where(m => m.ChatId == session.ChatId)
					.OrderByDescending(m => m.Id)
					.Select(m => (long?)m.Id)
					.FirstOrDefaultAsync(ct);
				session.ToolsPrunedThroughId = lastMsgId;
				await db.SaveChangesAsync(ct);

				if (lisOptions.Value.CompactionNotify) {
					int prunedEstimate = toolCount * 10; // ~10 tokens per pruned result
					int pct = totalInput > 0 ? (int)((long)(totalInput - toolTokens + prunedEstimate) * 100 / modelSettings.ContextBudget) : 0;
					int savings = toolTokens > 0 ? (int)((long)(toolTokens - prunedEstimate) * 100 / toolTokens) : 0;
					await ToolContext.NotifyAsync(
						$"🔧 Tool outputs pruned ({Fmt(toolTokens)} → {Fmt(prunedEstimate)}, -{savings}%)"
						+ $"\n  📊 Context: {Fmt(totalInput - toolTokens + prunedEstimate)}/{Fmt(modelSettings.ContextBudget)} ({pct}%)", ct);
				}
			}
		}
	}

	[Trace("ConversationService > EnsureSessionAsync")]
	private async Task<SessionEntity> EnsureSessionAsync(
		LisDbContext db, ChatEntity chat, long messageDbId, CancellationToken ct) {
		if (chat.CurrentSession is not null) return chat.CurrentSession;

		SessionEntity session = new() {
			ChatId         = chat.Id,
			StartMessageId = messageDbId > 0 ? messageDbId : null,
			CreatedAt      = DateTimeOffset.UtcNow,
			UpdatedAt      = DateTimeOffset.UtcNow
		};
		db.Sessions.Add(session);
		await db.SaveChangesAsync(ct);

		chat.CurrentSessionId = session.Id;
		chat.CurrentSession   = session;
		await db.SaveChangesAsync(ct);

		return session;
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
			Timestamp    = message.Timestamp,
			CreatedAt    = DateTimeOffset.UtcNow
		};

		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
		message.DbId = entity.Id;
	}

	private static async Task PersistSkMessageAsync(
		LisDbContext db, ChatEntity chat, ChatMessageContent msg,
		TokenUsage? usage, CancellationToken ct) {
		db.Messages.Add(new MessageEntity {
			ChatId              = chat.Id,
			SenderId            = "me",
			IsFromMe            = msg.Role != AuthorRole.User,
			Role                = msg.Role.Label,
			Body                = msg.Content,
			SkContent           = JsonSerializer.Serialize(msg),
			InputTokens         = usage?.InputTokens,
			OutputTokens        = usage?.OutputTokens,
			CacheReadTokens     = usage?.CacheReadTokens,
			CacheCreationTokens = usage?.CacheCreationTokens,
			ThinkingTokens      = usage?.ThinkingTokens,
			Timestamp           = DateTimeOffset.UtcNow,
			CreatedAt           = DateTimeOffset.UtcNow
		});
		await db.SaveChangesAsync(ct);
	}

	private static string Fmt(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";
}
