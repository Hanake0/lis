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

using Pgvector;

namespace Lis.Agent;

public sealed class CompactionService(
	[FromKeyedServices("compaction")] IChatClient compactionClient,
	ModelSettings                                  modelSettings,
	IServiceScopeFactory                          scopeFactory,
	IOptions<LisOptions>                          lisOptions,
	ILogger<CompactionService>                    logger,
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
				.Where(m => m.ChatId == chat.Id
				         && (session.StartMessageId == null || m.Id >= session.StartMessageId)
				         && m.Id <= splitMessageId)
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
			string summary = await this.SummarizeAsync(conversationText, ct);

			// Generate embedding
			Vector? embedding = null;
			if (embeddingGenerator is not null) {
				GeneratedEmbeddings<Embedding<float>> result =
					await embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
				if (result.Count > 0)
					embedding = new Vector(result[0].Vector);
			}

			// Finalize current session
			session.Summary          = summary;
			session.SummaryEmbedding = embedding;
			session.EndMessageId     = splitMessageId;
			session.IsCompacting     = false;
			session.UpdatedAt        = DateTimeOffset.UtcNow;

			// Create new session (continuation)
			SessionEntity newSession = new() {
				ChatId          = chat.Id,
				ParentSessionId = session.Id,
				StartMessageId  = splitMessageId + 1,
				CreatedAt       = DateTimeOffset.UtcNow,
				UpdatedAt       = DateTimeOffset.UtcNow
			};
			db.Sessions.Add(newSession);
			await db.SaveChangesAsync(ct);

			chat.CurrentSessionId = newSession.Id;
			await db.SaveChangesAsync(ct);

			// Notify user
			if (lisOptions.Value.CompactionNotify)
				await this.NotifyCompactionAsync(externalChatId, ct);

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
			currentSession.EndMessageId = await db.Messages
				.Where(m => m.ChatId == chat.Id)
				.OrderByDescending(m => m.Id)
				.Select(m => (long?)m.Id)
				.FirstOrDefaultAsync(ct);
			currentSession.UpdatedAt = DateTimeOffset.UtcNow;

			// Fire async summary generation for the old session (don't capture request ct)
			string chatExternalId = chat.ExternalId;
			long sessionId = currentSession.Id;
			if (currentSession.EndMessageId is not null) {
				_ = Task.Run(async () => {
					try {
						await this.GenerateSessionSummaryAsync(chatExternalId, sessionId, CancellationToken.None);
					} catch (Exception ex) {
						logger.LogError(ex, "Error generating summary for session #{SessionId}", sessionId);
					}
				}, CancellationToken.None);
			}
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
	private async Task GenerateSessionSummaryAsync(string externalChatId, long sessionId, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		SessionEntity? session = await db.Sessions.FindAsync([sessionId], ct);
		if (session is null) return;

		List<MessageEntity> messages = await db.Messages
			.Where(m => m.ChatId == session.ChatId
			         && (session.StartMessageId == null || m.Id >= session.StartMessageId)
			         && (session.EndMessageId == null || m.Id <= session.EndMessageId))
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		if (messages.Count == 0) return;

		string conversationText = BuildConversationText(messages, null);
		string summary = await this.SummarizeAsync(conversationText, ct);

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

	private async Task<string> SummarizeAsync(string conversationText, CancellationToken ct) {
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
		return result.Text ?? "";
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

	private async Task NotifyCompactionAsync(string chatId, CancellationToken ct) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			IChannelClient channel = scope.ServiceProvider.GetRequiredService<IChannelClient>();
			await channel.SendMessageAsync(chatId, "⚙️ Conversation compacted. Context optimized.", null, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to send compaction notification");
		}
	}
}
