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
	AgentService                 agentService,
	IMediaProcessor              mediaProcessor,
	IApprovalService             approvalService,
	ToolPolicyService            toolPolicyService,
	IOptions<LisOptions>         lisOptions,
	ILogger<ConversationService> logger,
	ITokenCounter?               tokenCounter = null) : IConversationService {

	[Trace("ConversationService > HandleIncomingAsync")]
	public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
		// Skip echoes of our own messages (tool notifications, AI responses).
		// All AI messages are already persisted by PersistSkMessageAsync with sk_content.
		if (message.IsFromMe) return;

		(_, bool shouldRespond) = await this.IngestMessageAsync(message, queued: false, ct);
		if (shouldRespond)
			await this.RespondAsync(message, ct);
	}

	public Task HandleTypingAsync(string chatId, CancellationToken ct) => Task.CompletedTask;

	[Trace("ConversationService > HandleSentEchoAsync")]
	public async Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		MessageEntity? msg = await db.Messages
			.FirstOrDefaultAsync(m => m.ExternalId == echo.ExternalId, ct);
		if (msg is null) return;

		msg.SenderId   = echo.SenderId;
		msg.SenderName = echo.SenderName;
		msg.Timestamp  = echo.Timestamp;
		await db.SaveChangesAsync(ct);
	}

	[Trace("ConversationService > HandleReactionAsync")]
	public async Task HandleReactionAsync(string messageId, string chatId, string emoji, string senderId, CancellationToken ct) {
		// Only owner reactions resolve approvals
		if (senderId != lisOptions.Value.OwnerJid) return;

		ApprovalDecision? decision = emoji switch {
			"👍" => ApprovalDecision.Once,
			"✅" => ApprovalDecision.Always,
			"❌" => ApprovalDecision.Deny,
			_    => null
		};

		if (decision is null) return;

		await approvalService.ResolveByMessageAsync(messageId, decision.Value, senderId);
	}

	[Trace("ConversationService > IngestMessageAsync")]
	public async Task<(ChatEntity Chat, bool ShouldRespond)> IngestMessageAsync(
		IncomingMessage message, bool queued, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity chat = await UpsertChatAsync(db, message, lisOptions.Value.OwnerJid, ct);
		SessionEntity session = await this.EnsureSessionAsync(db, chat, ct);
		await PersistMessageAsync(db, session, message, queued, ct);

		if (message.MediaType is not null)
			await this.ProcessMediaAsync(db, message, ct);

		try {
			await channelClient.MarkReadAsync(message.ExternalId, message.ChatId, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to mark message as read");
		}

		// Full auth: mention detection + gate check (single entry point)
		bool shouldRespond = await agentService.ShouldRespondAsync(db, chat, message, lisOptions.Value.OwnerJid, ct);
		return (chat, shouldRespond);
	}

	[Trace("ConversationService > RespondAsync")]
	public async Task RespondAsync(IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity? chat = await db.Chats
			.Include(c => c.CurrentSession)
			.Include(c => c.AllowedSenders)
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == message.ChatId, ct);

		if (chat is null) {
			logger.LogWarning("Chat not found for {ChatId} during respond phase", message.ChatId);
			return;
		}

		AgentEntity    agent              = await agentService.ResolveForChatAsync(db, chat, ct);
		ModelSettings  agentModelSettings = AgentService.ToModelSettings(agent);
		SessionEntity  session            = chat.CurrentSession!;

		// Handle commands before AI processing
		if (commandRouter.Match(message.Body) is { } match) {
			if (match.Command.OwnerOnly && message.SenderId != lisOptions.Value.OwnerJid) {
				string denied = "⛔ This command requires owner authorization.";
				await channelClient.SendMessageAsync(message.ChatId, denied, message.ExternalId, ct);

				db.Messages.Add(new MessageEntity {
					ChatId    = chat.Id,
					SessionId = session.Id,
					SenderId  = "me",
					IsFromMe  = true,
					Role      = "assistant",
					Body      = denied,
					Timestamp = DateTimeOffset.UtcNow,
					CreatedAt = DateTimeOffset.UtcNow
				});
				await db.SaveChangesAsync(ct);
				return;
			}

			CommandContext ctx = new(message, chat, session, db, agent, match.Args);
			string response = await match.Command.ExecuteAsync(ctx, ct);
			await channelClient.SendMessageAsync(message.ChatId, response, message.ExternalId, ct);

			// Persist so AI sees the response in history
			db.Messages.Add(new MessageEntity {
				ChatId    = chat.Id,
				SessionId = session.Id,
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

		// Load messages from current session (exclude queued — they're not yet visible to AI)
		List<MessageEntity> recentMessages = await db.Messages
			.Where(m => m.SessionId == session.Id && !m.Queued)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		string systemPrompt = await promptComposer.BuildAsync(db, agent.Id, ct, chat.IsGroup, agent);

		// Load parent session for continuity
		SessionEntity? parentSession = session.ParentSessionId is not null
			? await db.Sessions.FindAsync([session.ParentSessionId], ct)
			: null;

		ChatHistory chatHistory = contextWindowBuilder.Build(
			systemPrompt, recentMessages, session, parentSession, lisOptions.Value, chat);

		// Pre-send validation: count tokens when context is likely large
		if (tokenCounter is not null && session.ContextTokens > agentModelSettings.ContextBudget * 0.7) {
			try {
				string countJson = ChatHistorySerializer.ToAnthropicJson(chatHistory, agentModelSettings.Model);
				int? tokenCount = await tokenCounter.CountAsync(countJson, ct);
				if (tokenCount > agentModelSettings.ContextBudget)
					logger.LogWarning("Pre-send token count ({Tokens}) exceeds budget ({Budget})",
						tokenCount, agentModelSettings.ContextBudget);
			} catch (Exception ex) {
				logger.LogWarning(ex, "Pre-send token counting failed");
			}
		}

		ToolContext.ChatId               = message.ChatId;
		ToolContext.Channel              = channelClient;
		ToolContext.MessageExternalId    = message.ExternalId;
		ToolContext.NotificationsEnabled = agent.ToolNotifications;
		ToolContext.AgentId              = agent.Id;
		ToolContext.SenderJid            = message.SenderId;
		ToolContext.IsOwner              = message.SenderId == lisOptions.Value.OwnerJid;

		IChatCompletionService chatService = kernel.GetRequiredService<IChatCompletionService>();

		Dictionary<string, object> extensionData = new() { ["max_tokens"] = agentModelSettings.MaxTokens };
		if (agentModelSettings.ThinkingEffort is { Length: > 0 } effort)
			extensionData["thinking"] = new Dictionary<string, object> {
				["type"] = "enabled",
				["budget_tokens"] = effort switch {
					"low"    => 1024,
					"medium" => 4096,
					"high"   => 16384,
					_ => int.TryParse(effort, out int t) ? t : 4096
				}
			};

		// Clone kernel and strip plugins not in the agent's tool policy.
		// FunctionChoiceBehavior.Auto(functions:) breaks Anthropic SDK serialization,
		// so we filter at the kernel level instead — SK auto-discovers from plugins.
		Kernel agentKernel = kernel.Clone();
		HashSet<string> allowedPlugins = toolPolicyService.GetAllowedPluginNames(agent);
		foreach (KernelPlugin plugin in agentKernel.Plugins.ToList())
			if (!allowedPlugins.Contains(plugin.Name))
				agentKernel.Plugins.Remove(plugin);

		PromptExecutionSettings settings = new() {
			ModelId                = agentModelSettings.Model,
			FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
			ExtensionData          = extensionData
		};

		TokenUsage? lastUsage = null;
		TokenUsage? prevUsage = null;
		List<long>  pendingToolMsgIds = new();

		await foreach (ChatMessageContent msg in toolRunner.RunAsync(chatService, chatHistory, agentKernel, settings, ct)) {
			string? externalId = null;
			if (msg.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(msg.Content)) {
				(string? content, bool shouldQuote) = ResponseDirectives.Parse(msg.Content);
				if (content is not null)
					externalId = await channelClient.SendMessageAsync(
						message.ChatId, content, shouldQuote ? message.ExternalId : null, ct);
			}

			// Usage is attached per-message by ToolRunner (only on assistant messages)
			TokenUsage? msgUsage = ToolRunner.GetUsage(msg);

			// Back-fill tool result token counts from the API input delta
			if (msgUsage is not null && prevUsage is not null && pendingToolMsgIds.Count > 0) {
				int contentOutput = prevUsage.OutputTokens - prevUsage.ThinkingTokens;
				int delta = msgUsage.TotalInputTokens - prevUsage.TotalInputTokens - contentOutput;
				if (delta > 0)
					await BackfillToolTokensAsync(db, pendingToolMsgIds, delta, ct);
				pendingToolMsgIds.Clear();
			}

			if (msgUsage is not null) { prevUsage = msgUsage; lastUsage = msgUsage; }

			long entityId = await PersistSkMessageAsync(db, chat, session, msg, msgUsage, externalId, ct);

			if (msg.Role == AuthorRole.Tool)
				pendingToolMsgIds.Add(entityId);
		}

		// Update session token stats from last response
		if (lastUsage is not null) {
			// Reload session — compaction may have completed during the AI loop
			await db.Entry(session).ReloadAsync(ct);

			// If session was finalized by compaction, skip updates (new session is current)
			ChatEntity? reloadedChat = await db.Chats.FindAsync([chat.Id], ct);
			if (reloadedChat is not null && reloadedChat.CurrentSessionId != session.Id) return;

			session.TotalInputTokens         += lastUsage.InputTokens;
			session.TotalOutputTokens        += lastUsage.OutputTokens;
			session.TotalCacheReadTokens     += lastUsage.CacheReadTokens;
			session.TotalCacheCreationTokens += lastUsage.CacheCreationTokens;
			session.TotalThinkingTokens      += lastUsage.ThinkingTokens;
			session.ContextTokens             = lastUsage.TotalInputTokens;
			session.UpdatedAt                 = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync(ct);

			await this.CheckCompactionTriggersAsync(db, session, agent, lastUsage, message.ChatId, ct);
		}
	}

	private async Task CheckCompactionTriggersAsync(
		LisDbContext db, SessionEntity session, AgentEntity agent, TokenUsage usage, string chatId, CancellationToken ct) {
		int totalInput = usage.TotalInputTokens;
		int thresholdPct = agent.CompactionThreshold > 0 ? agent.CompactionThreshold : 80;
		int compactionThreshold = (int)(agent.ContextBudget * (thresholdPct / 100.0));

		// Full compaction takes priority — calculate split point from recent messages
		if (totalInput > compactionThreshold && !session.IsCompacting) {
			// Safeguard: also set tool prune boundary so keep_all yields to auto
			if (session.ToolsPrunedThroughId is null) {
				long? lastMsgId = await db.Messages
					.Where(m => m.ChatId == session.ChatId && !m.Queued)
					.OrderByDescending(m => m.Id)
					.Select(m => (long?)m.Id)
					.FirstOrDefaultAsync(ct);
				session.ToolsPrunedThroughId = lastMsgId;
				await db.SaveChangesAsync(ct);
			}

			// Find split: walk backwards from newest, keep KeepRecentTokens
			List<MessageEntity> allMsgs = await db.Messages
				.Where(m => m.SessionId == session.Id && !m.Queued)
				.OrderByDescending(m => m.Id)
				.ToListAsync(ct);

			long splitId = CompactionService.CalculateSplitPoint(allMsgs, agent.KeepRecentTokens);

			if (splitId > 0)
				_ = Task.Run(() => compactionService.CompactAsync(chatId, splitId, CancellationToken.None), CancellationToken.None);
			return;
		}

		// Tool pruning — count only tool result message tokens
		if (session.ToolsPrunedThroughId is null) {
			int toolTokens = await db.Messages
				.Where(m => m.SessionId == session.Id && !m.Queued && m.Role == "tool")
				.SumAsync(m => m.OutputTokens ?? 0, ct);

			if (toolTokens > agent.ToolPruneThreshold) {
				int toolCount = await db.Messages
					.Where(m => m.SessionId == session.Id && !m.Queued && m.Role == "tool")
					.CountAsync(ct);

				long? lastMsgId = await db.Messages
					.Where(m => m.ChatId == session.ChatId && !m.Queued)
					.OrderByDescending(m => m.Id)
					.Select(m => (long?)m.Id)
					.FirstOrDefaultAsync(ct);
				session.ToolsPrunedThroughId = lastMsgId;
				await db.SaveChangesAsync(ct);

				if (lisOptions.Value.CompactionNotify) {
					int prunedEstimate = toolCount * 10; // ~10 tokens per pruned result
					int pct = totalInput > 0 ? (int)((long)(totalInput - toolTokens + prunedEstimate) * 100 / agent.ContextBudget) : 0;
					int savings = toolTokens > 0 ? (int)((long)(toolTokens - prunedEstimate) * 100 / toolTokens) : 0;
					await ToolContext.NotifyAsync(
						$"🔧 Tool outputs pruned ({Fmt(toolTokens)} → {Fmt(prunedEstimate)}, -{savings}%)"
						+ $"\n  📊 Context: {Fmt(totalInput - toolTokens + prunedEstimate)}/{Fmt(agent.ContextBudget)} ({pct}%)", ct);
				}
			}
		}
	}

	[Trace("ConversationService > EnsureSessionAsync")]
	private async Task<SessionEntity> EnsureSessionAsync(
		LisDbContext db, ChatEntity chat, CancellationToken ct) {
		if (chat.CurrentSession is not null) return chat.CurrentSession;

		SessionEntity session = new() {
			ChatId    = chat.Id,
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		db.Sessions.Add(session);
		await db.SaveChangesAsync(ct);

		chat.CurrentSessionId = session.Id;
		chat.CurrentSession   = session;
		await db.SaveChangesAsync(ct);

		return session;
	}

	private static async Task<ChatEntity> UpsertChatAsync(
		LisDbContext db, IncomingMessage message, string ownerJid, CancellationToken ct) {
		ChatEntity? chat = await db.Chats
								   .Include(c => c.CurrentSession)
								   .Include(c => c.AllowedSenders)
								   .Include(c => c.Agent)
								   .FirstOrDefaultAsync(c => c.ExternalId == message.ChatId, ct);

		if (chat is null) {
			string? chatName = message.IsGroup ? message.ChatName : null;

			chat = new ChatEntity {
				ExternalId     = message.ChatId,
				Name           = chatName ?? message.SenderName,
				IsGroup        = message.IsGroup,
				RequireMention = message.IsGroup,
				Enabled        = message.IsGroup || message.SenderId == ownerJid,
				CreatedAt      = DateTimeOffset.UtcNow,
				UpdatedAt      = DateTimeOffset.UtcNow
			};
			db.Chats.Add(chat);
			await db.SaveChangesAsync(ct);
		} else {
			chat.UpdatedAt = DateTimeOffset.UtcNow;

			if (message.IsGroup && message.ChatName is not null)
				chat.Name = message.ChatName;
			else if (message.SenderName is not null)
				chat.Name = message.SenderName;

			await db.SaveChangesAsync(ct);
		}

		return chat;
	}

	private static async Task PersistMessageAsync(
		LisDbContext db, SessionEntity session, IncomingMessage message, bool queued, CancellationToken ct) {
		MessageEntity entity = new() {
			ExternalId   = message.ExternalId,
			ChatId       = session.ChatId,
			SessionId    = session.Id,
			SenderId     = message.SenderId,
			SenderName   = message.SenderName,
			IsFromMe     = message.IsFromMe,
			Body         = message.Body,
			MediaType    = message.MediaType,
			MediaCaption = message.MediaCaption,
			ReplyToId      = message.RepliedId,
			ReplyContent   = message.RepliedContent,
			Queued         = queued,
			Timestamp    = message.Timestamp,
			CreatedAt    = DateTimeOffset.UtcNow
		};

		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
		message.DbId = entity.Id;
	}

	private static async Task<long> PersistSkMessageAsync(
		LisDbContext db, ChatEntity chat, SessionEntity session,
		ChatMessageContent msg, TokenUsage? usage, string? externalId, CancellationToken ct) {
		MessageEntity entity = new() {
			ExternalId          = externalId,
			ChatId              = chat.Id,
			SessionId           = session.Id,
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
		};
		db.Messages.Add(entity);
		await db.SaveChangesAsync(ct);
		return entity.Id;
	}

	/// <summary>
	/// Distributes the API input token delta proportionally among tool result messages
	/// using a local BPE tokenizer for relative weights.
	/// </summary>
	private static async Task BackfillToolTokensAsync(
		LisDbContext db, List<long> toolMsgIds, int totalDelta, CancellationToken ct) {
		List<MessageEntity> toolMsgs = await db.Messages
			.Where(m => toolMsgIds.Contains(m.Id))
			.ToListAsync(ct);

		int[] rawCounts = toolMsgs
			.Select(m => TokenEstimator.Count(m.SkContent ?? m.Body))
			.ToArray();
		int rawTotal = rawCounts.Sum();

		for (int i = 0; i < toolMsgs.Count; i++) {
			toolMsgs[i].OutputTokens = rawTotal > 0
				? (int)((long)totalDelta * rawCounts[i] / rawTotal)
				: toolMsgs.Count > 0 ? totalDelta / toolMsgs.Count : totalDelta;
		}

		await db.SaveChangesAsync(ct);
	}

	private async Task ProcessMediaAsync(LisDbContext db, IncomingMessage message, CancellationToken ct) {
		try {
			ProcessedMedia? media = await mediaProcessor.ProcessAsync(message, ct);
			if (media is null) return;

			MessageEntity? entity = await db.Messages.FindAsync([message.DbId], ct);
			if (entity is null) return;

			entity.MediaData     = media.Data;
			entity.MediaMimeType = media.MimeType;

			if (media.Transcription is { Length: > 0 } transcript)
				entity.Body = $"<Audio transcript: {transcript}>";
			else if (message.MediaType is "audio" or "ptt")
				entity.Body = "<Audio message>";

			await db.SaveChangesAsync(ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to process media for {Id}", message.ExternalId);
		}
	}

	private static string Fmt(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";
}
