using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

using Pgvector;

namespace Lis.Agent;

public sealed class CompactionService(
	[FromKeyedServices("compaction")] IChatClient compactionClient,
	ModelSettings                                  modelSettings,
	IServiceScopeFactory                          scopeFactory,
	IOptions<LisOptions>                          lisOptions,
	ILogger<CompactionService>                    logger,
	PromptComposer                                promptComposer,
	ContextWindowBuilder                          contextWindowBuilder,
	ITokenCounter?                                tokenCounter = null,
	IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null) {

	[Trace("CompactionService > CompactAsync")]
	public async Task CompactAsync(string externalChatId, long splitMessageId, CancellationToken ct) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			ChatEntity? chat = await db.Chats
				.Include(c => c.CurrentSession)
				.FirstOrDefaultAsync(c => c.ExternalId == externalChatId, ct);

			if (chat?.CurrentSession is null) return;

			SessionEntity session = chat.CurrentSession;

			// Atomic claim — prevents concurrent compactions
			int claimed = await db.Database.ExecuteSqlInterpolatedAsync(
				$"UPDATE session SET is_compacting = true, updated_at = {DateTimeOffset.UtcNow} WHERE id = {session.Id} AND is_compacting = false", ct);
			if (claimed == 0) return;
			await db.Entry(session).ReloadAsync(ct);

			// Load messages from session start to split point
			List<MessageEntity> messages = await db.Messages
				.Where(m => m.SessionId == session.Id && m.Id <= splitMessageId)
				.OrderBy(m => m.Timestamp)
				.ToListAsync(ct);

			if (messages.Count == 0) {
				session.IsCompacting = false;
				await db.SaveChangesAsync(ct);
				return;
			}

			// Build conversation text for summarization
			string conversationText = BuildConversationText(messages, session.Summary);

			// Call compaction LLM
			(string summary, int summaryTokens) = await this.SummarizeAsync(conversationText, ct);

			// Generate embedding
			Vector? embedding = null;
			if (embeddingGenerator is not null) {
				GeneratedEmbeddings<Embedding<float>> result =
					await embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
				if (result.Count > 0)
					embedding = new Vector(result[0].Vector);
			}

			// Finalize current session + create new session + reassign messages atomically
			await using var transaction = await db.Database.BeginTransactionAsync(ct);

			session.Summary          = summary;
			session.SummaryEmbedding = embedding;
			session.IsCompacting     = false;
			session.UpdatedAt        = DateTimeOffset.UtcNow;

			SessionEntity newSession = new() {
				ChatId          = chat.Id,
				ParentSessionId = session.Id,
				CreatedAt       = DateTimeOffset.UtcNow,
				UpdatedAt       = DateTimeOffset.UtcNow
			};
			db.Sessions.Add(newSession);
			chat.CurrentSessionId = newSession.Id;
			await db.SaveChangesAsync(ct);

			// Reassign kept messages to new session
			await db.Database.ExecuteSqlInterpolatedAsync(
				$"UPDATE message SET session_id = {newSession.Id} WHERE session_id = {session.Id} AND id > {splitMessageId}", ct);

			await transaction.CommitAsync(ct);

			// Compute new context stats for notification
			int keptTokens = await db.Messages
				.Where(m => m.SessionId == newSession.Id)
				.SumAsync(m => (m.OutputTokens ?? m.InputTokens ?? 0), ct);
			int toolTokens = await db.Messages
				.Where(m => m.SessionId == newSession.Id && m.Role == "tool")
				.SumAsync(m => m.OutputTokens ?? 0, ct);

			// Count actual tokens for the new session's context
			int? actualTotal = null;
			if (tokenCounter is not null) {
				try {
					string systemPrompt = await promptComposer.BuildAsync(db, ct);
					List<MessageEntity> keptMessages = await db.Messages
						.Where(m => m.SessionId == newSession.Id)
						.OrderBy(m => m.Timestamp)
						.ToListAsync(ct);
					ChatHistory newCtx = contextWindowBuilder.Build(
						systemPrompt, keptMessages, newSession, session, lisOptions.Value);
					string json = ChatHistorySerializer.ToAnthropicJson(newCtx, modelSettings.Model);
					actualTotal = await tokenCounter.CountAsync(json, ct);
				} catch (Exception ex) {
					logger.LogWarning(ex, "Token counting failed; using estimation");
				}
			}

			// Notify user
			if (lisOptions.Value.CompactionNotify)
				await this.NotifyCompactionAsync(
					externalChatId, session.ContextTokens,
					summaryTokens, keptTokens, toolTokens, actualTotal, ct);

			if (logger.IsEnabled(LogLevel.Information))
				logger.LogInformation(
					"Compacted session #{OldSession} to #{NewSession} for chat {ChatId}",
					session.Id, newSession.Id, externalChatId);

		} catch (Exception ex) {
			logger.LogError(ex, "Error during compaction for chat {ChatId}", externalChatId);

			// Reset compacting flag
			try {
				using IServiceScope scope = scopeFactory.CreateScope();
				LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();
				ChatEntity? chat = await db.Chats
					.Include(c => c.CurrentSession)
					.FirstOrDefaultAsync(c => c.ExternalId == externalChatId, ct);
				if (chat?.CurrentSession is not null) {
					chat.CurrentSession.IsCompacting = false;
					await db.SaveChangesAsync(ct);
				}
			} catch {
				// Best effort cleanup
			}
		}
	}

	/// <summary>
	/// Creates a new session, optionally finalizing the current one with a summary.
	/// Used by /new and /clear commands.
	/// </summary>
	[Trace("CompactionService > StartNewSessionAsync")]
	public async Task<SessionEntity> StartNewSessionAsync(
		ChatEntity chat, SessionEntity? currentSession,
		bool isExplicitBreak, LisDbContext db, CancellationToken ct) {

		// Finalize current session if it exists
		if (currentSession is not null) {
			currentSession.UpdatedAt = DateTimeOffset.UtcNow;

			// Fire async summary generation for the old session (don't capture request ct)
			string chatExternalId = chat.ExternalId;
			long sessionId = currentSession.Id;
			_ = Task.Run(async () => {
				try {
					await this.GenerateSessionSummaryAsync(sessionId, CancellationToken.None);
				} catch (Exception ex) {
					logger.LogError(ex, "Error generating summary for session #{SessionId}", sessionId);
				}
			}, CancellationToken.None);
		}

		// Create new session
		SessionEntity newSession = new() {
			ChatId          = chat.Id,
			ParentSessionId = isExplicitBreak ? null : currentSession?.Id,
			CreatedAt       = DateTimeOffset.UtcNow,
			UpdatedAt       = DateTimeOffset.UtcNow
		};
		db.Sessions.Add(newSession);
		await db.SaveChangesAsync(ct);

		chat.CurrentSessionId = newSession.Id;
		await db.SaveChangesAsync(ct);

		return newSession;
	}

	[Trace("CompactionService > GenerateSessionSummaryAsync")]
	public async Task GenerateSessionSummaryAsync(long sessionId, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		SessionEntity? session = await db.Sessions.FindAsync([sessionId], ct);
		if (session is null) return;

		List<MessageEntity> messages = await db.Messages
			.Where(m => m.SessionId == session.Id)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		if (messages.Count == 0) return;

		string conversationText = BuildConversationText(messages, null);
		(string summary, _) = await this.SummarizeAsync(conversationText, ct);

		Vector? embedding = null;
		if (embeddingGenerator is not null) {
			GeneratedEmbeddings<Embedding<float>> result =
				await embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
			if (result.Count > 0)
				embedding = new Vector(result[0].Vector);
		}

		session.Summary          = summary;
		session.SummaryEmbedding = embedding;
		session.UpdatedAt        = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(ct);
	}

	private async Task<(string Text, int OutputTokens)> SummarizeAsync(string conversationText, CancellationToken ct) {
		string prompt = $"""
			Summarize the following conversation concisely. Preserve:
			- Key facts, names, dates, and decisions made
			- User preferences and standing instructions
			- Ongoing tasks or commitments
			- Emotional tone and relationship context
			- Important results from tool usage

			Discard: greetings, repetitive exchanges, raw tool call metadata, verbose tool outputs.

			Conversation:
			{conversationText}
			""";

		string model = lisOptions.Value.CompactionModel is { Length: > 0 } m ? m : modelSettings.Model;
		ChatOptions options = new() { ModelId = model };
		ChatResponse result = await compactionClient.GetResponseAsync(prompt, options, ct);
		int outputTokens = (int)(result.Usage?.OutputTokenCount ?? 0);
		return (result.Text ?? "", outputTokens);
	}

	private static string BuildConversationText(IReadOnlyList<MessageEntity> messages, string? existingSummary) {
		System.Text.StringBuilder sb = new();

		if (existingSummary is { Length: > 0 })
			sb.AppendLine($"Previous summary:\n{existingSummary}\n\nNew messages:");

		foreach (MessageEntity msg in messages) {
			string role = msg.IsFromMe ? "Assistant" : "User";
			string body = msg.Body ?? "[media/tool]";
			sb.AppendLine($"{role}: {body}");
		}

		return sb.ToString();
	}

	private async Task NotifyCompactionAsync(
		string chatId, long oldInputTokens,
		int summaryTokens, int keptTokens, int toolTokens,
		int? actualTotal, CancellationToken ct) {
		try {
			int budget = modelSettings.ContextBudget;
			int newTotal = actualTotal ?? (summaryTokens + keptTokens);
			int pct = budget > 0 ? (int)((long)newTotal * 100 / budget) : 0;

			string msg = $"⚙️ Compacted ({FormatTokens(oldInputTokens)} → {FormatTokens(newTotal)})";

			// System tokens = actual total - summary - kept (only when actual count available)
			if (actualTotal is not null) {
				int systemTokens = actualTotal.Value - summaryTokens - keptTokens;
				if (systemTokens > 0)
					msg += $"\n  🔧 System: {FormatTokens(systemTokens)} tokens";
			}

			msg += $"\n  📝 Summary: {FormatTokens(summaryTokens)} tokens"
			     + $"\n  💬 Kept context: {FormatTokens(keptTokens - toolTokens)} tokens"
			     + $"\n  🛠️ Tools: {FormatTokens(toolTokens)} tokens"
			     + $"\n  📊 Total: {FormatTokens(newTotal)}/{FormatTokens(budget)} ({pct}%)";

			using IServiceScope scope = scopeFactory.CreateScope();
			IChannelClient channel = scope.ServiceProvider.GetRequiredService<IChannelClient>();
			await channel.SendMessageAsync(chatId, msg, null, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to send compaction notification");
		}
	}

	private static string FormatTokens(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";

	/// <summary>
	/// Walks messages (newest-first) accumulating token costs.
	/// Returns the ID of the first message that exceeds the keep budget — everything
	/// at or before this ID should be compacted. Messages without token counts
	/// contribute 0 (no estimation).
	/// </summary>
	public static long CalculateSplitPoint(IReadOnlyList<MessageEntity> messagesNewestFirst, int keepRecentTokens) {
		if (messagesNewestFirst.Count == 0) return 0;

		long splitId = messagesNewestFirst[^1].Id;
		int accumulated = 0;
		foreach (MessageEntity m in messagesNewestFirst) {
			accumulated += m.OutputTokens ?? m.InputTokens ?? 0;
			if (accumulated > keepRecentTokens) {
				splitId = m.Id;
				break;
			}
		}
		return splitId;
	}
}
