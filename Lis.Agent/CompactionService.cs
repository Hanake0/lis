using System.Text.Json.Nodes;

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
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using FunctionCallContent = Microsoft.SemanticKernel.FunctionCallContent;
using FunctionResultContent = Microsoft.SemanticKernel.FunctionResultContent;

using Pgvector;

namespace Lis.Agent;

public sealed class CompactionService(
	[FromKeyedServices("compaction")] IChatClient compactionClient,
	AgentService                                  agentService,
	IServiceScopeFactory                          scopeFactory,
	IOptions<LisOptions>                          lisOptions,
	ILogger<CompactionService>                    logger,
	PromptComposer                                promptComposer,
	ContextWindowBuilder                          contextWindowBuilder,
	Kernel                                        kernel,
	ITokenCounter?                                tokenCounter = null,
	IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null) {

	[Trace("CompactionService > CompactAsync")]
	public async Task CompactAsync(string externalChatId, long splitMessageId, CancellationToken ct) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			ChatEntity? chat = await db.Chats
				.Include(c => c.CurrentSession)
				.Include(c => c.Agent)
				.FirstOrDefaultAsync(c => c.ExternalId == externalChatId, ct);

			if (chat?.CurrentSession is null) return;

			AgentEntity   agent              = await agentService.ResolveForChatAsync(db, chat, ct);
			ModelSettings agentModelSettings = AgentService.ToModelSettings(agent);
			SessionEntity session            = chat.CurrentSession;

			// Atomic claim — prevents concurrent compactions
			int claimed = await db.Database.ExecuteSqlInterpolatedAsync(
				$"UPDATE session SET is_compacting = true, updated_at = {DateTimeOffset.UtcNow} WHERE id = {session.Id} AND is_compacting = false", ct);
			if (claimed == 0) return;
			await db.Entry(session).ReloadAsync(ct);

			// Load messages from session start to split point
			List<MessageEntity> messages = await db.Messages
				.Where(m => m.SessionId == session.Id && !m.Queued && m.Id <= splitMessageId)
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
			(string summary, int summaryTokens) = await this.SummarizeAsync(conversationText, agentModelSettings.Model, ct);

			// Generate embedding
			Vector? embedding = null;
			if (embeddingGenerator is not null) {
				GeneratedEmbeddings<Embedding<float>> result =
					await embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
				if (result.Count > 0)
					embedding = new Vector(result[0].Vector);
			}

			// Finalize current session + create new session + reassign messages atomically
			SessionEntity newSession = null!;
			var strategy = db.Database.CreateExecutionStrategy();
			await strategy.ExecuteAsync(async () => {
				await using var transaction = await db.Database.BeginTransactionAsync(ct);

				session.Summary          = summary;
				session.SummaryEmbedding = embedding;
				session.IsCompacting     = false;
				session.UpdatedAt        = DateTimeOffset.UtcNow;

				newSession = new() {
					ChatId          = chat.Id,
					ParentSessionId = session.Id,
					CreatedAt       = DateTimeOffset.UtcNow,
					UpdatedAt       = DateTimeOffset.UtcNow
				};
				db.Sessions.Add(newSession);
				await db.SaveChangesAsync(ct);

				chat.CurrentSessionId = newSession.Id;
				await db.SaveChangesAsync(ct);

				// Reassign kept messages to new session
				await db.Database.ExecuteSqlInterpolatedAsync(
					$"UPDATE message SET session_id = {newSession.Id} WHERE session_id = {session.Id} AND id > {splitMessageId}", ct);

				await transaction.CommitAsync(ct);
			});

			// Build new context window for token counting and breakdown
			if (lisOptions.Value.CompactionNotify) {
				try {
					string systemPrompt = await promptComposer.BuildAsync(db, agent.Id, ct);
					List<MessageEntity> keptMessages = await db.Messages
						.Where(m => m.SessionId == newSession.Id)
						.OrderBy(m => m.Timestamp)
						.ToListAsync(ct);
					ChatHistory newCtx = contextWindowBuilder.Build(
						systemPrompt, keptMessages, newSession, session, lisOptions.Value);

					// Count tokens with and without tool definitions to measure tool def cost
					int? totalWithTools = null;
					int? totalWithoutTools = null;
					if (tokenCounter is not null) {
						string jsonWithTools = ChatHistorySerializer.ToAnthropicJson(newCtx, agentModelSettings.Model, kernel);
						totalWithTools = await tokenCounter.CountAsync(jsonWithTools, ct);
						string jsonNoTools = ChatHistorySerializer.ToAnthropicJson(newCtx, agentModelSettings.Model);
						totalWithoutTools = await tokenCounter.CountAsync(jsonNoTools, ct);
					}

					int total = totalWithTools ?? EstimateTotalTokens(newCtx, kernel);
					int toolDefTokens = (totalWithTools is not null && totalWithoutTools is not null)
						? totalWithTools.Value - totalWithoutTools.Value
						: EstimateToolDefTokens(kernel);
					int contentTotal = total - toolDefTokens;
					(int sysTokens, int sumTokens, int keptTokens, int toolCallTokens) =
						EstimateBreakdown(newCtx, contentTotal);

					await this.NotifyCompactionAsync(
						externalChatId, agent, session.ContextTokens, total,
						sysTokens, sumTokens, keptTokens, toolDefTokens, toolCallTokens, ct);
				} catch (Exception ex) {
					logger.LogWarning(ex, "Token counting or notification failed");
				}
			}

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

		ChatEntity? chat = await db.Chats
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.Id == session.ChatId, ct);
		if (chat is null) return;

		AgentEntity   agent              = await agentService.ResolveForChatAsync(db, chat, ct);
		ModelSettings agentModelSettings = AgentService.ToModelSettings(agent);

		List<MessageEntity> messages = await db.Messages
			.Where(m => m.SessionId == session.Id && !m.Queued)
			.OrderBy(m => m.Timestamp)
			.ToListAsync(ct);

		if (messages.Count == 0) return;

		string conversationText = BuildConversationText(messages, null);
		(string summary, _) = await this.SummarizeAsync(conversationText, agentModelSettings.Model, ct);

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

	private async Task<(string Text, int OutputTokens)> SummarizeAsync(string conversationText, string agentModel, CancellationToken ct) {
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

		string model = lisOptions.Value.CompactionModel is { Length: > 0 } m ? m : agentModel;
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
		string chatId, AgentEntity agent, long oldInputTokens,
		int newTotal, int systemTokens, int summaryTokens, int keptTokens,
		int toolDefTokens, int toolCallTokens, CancellationToken ct) {
		try {
			int budget = agent.ContextBudget;
			int pct = budget > 0 ? (int)((long)newTotal * 100 / budget) : 0;
			int totalToolTokens = toolDefTokens + toolCallTokens;

			string msg = $"⚙️ Compacted ({FormatTokens(oldInputTokens)} → {FormatTokens(newTotal)})"
			           + $"\n  🔧 System: {FormatTokens(systemTokens)} tokens"
			           + $"\n  📝 Summary: {FormatTokens(summaryTokens)} tokens"
			           + $"\n  💬 Kept context: {FormatTokens(keptTokens)} tokens"
			           + $"\n  🛠️ Tools: {FormatTokens(totalToolTokens)} tokens ({FormatTokens(toolDefTokens)} defs + {FormatTokens(toolCallTokens)} calls)"
			           + $"\n  📊 Total: {FormatTokens(newTotal)}/{FormatTokens(budget)} ({pct}%)";

			using IServiceScope scope = scopeFactory.CreateScope();
			IChannelClient channel = scope.ServiceProvider.GetRequiredService<IChannelClient>();
			await channel.SendMessageAsync(chatId, msg, null, ct);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Failed to send compaction notification");
		}
	}

	/// <summary>
	/// Estimates token breakdown for system/summary/kept/tool-calls by measuring
	/// character lengths and scaling proportionally to the known total.
	/// Tool definitions are excluded (counted separately via tokenCounter).
	/// </summary>
	private static (int System, int Summary, int Kept, int ToolCalls) EstimateBreakdown(ChatHistory history, int totalTokens) {
		int systemChars = 0, summaryChars = 0, toolCallChars = 0, keptChars = 0;

		foreach (ChatMessageContent msg in history) {
			int chars = MeasureContentChars(msg);

			if (msg.Role == AuthorRole.System) {
				systemChars += chars;
			} else if (msg.Role == AuthorRole.Assistant && (msg.Content?.StartsWith("Here is context from") ?? false)) {
				summaryChars += chars;
			} else if (msg.Role == AuthorRole.Tool || msg.Items.OfType<FunctionCallContent>().Any()) {
				toolCallChars += chars;
			} else {
				keptChars += chars;
			}
		}

		int totalChars = systemChars + summaryChars + toolCallChars + keptChars;
		if (totalChars == 0) return (totalTokens, 0, 0, 0);

		int sys = (int)((long)totalTokens * systemChars / totalChars);
		int sum = (int)((long)totalTokens * summaryChars / totalChars);
		int tools = (int)((long)totalTokens * toolCallChars / totalChars);
		int kept = totalTokens - sys - sum - tools;

		return (sys, sum, kept, tools);
	}

	private static int MeasureContentChars(ChatMessageContent msg) {
		int chars = msg.Content?.Length ?? 0;
		foreach (KernelContent item in msg.Items) {
			if (item is FunctionCallContent fc) {
				chars += fc.FunctionName?.Length ?? 0;
				if (fc.Arguments is not null)
					chars += System.Text.Json.JsonSerializer.Serialize(fc.Arguments).Length;
			} else if (item is FunctionResultContent fr) {
				chars += fr.Result?.ToString()?.Length ?? 0;
				chars += fr.FunctionName?.Length ?? 0;
			}
		}
		return chars;
	}

	private static int EstimateTotalTokens(ChatHistory history, Kernel? kernel) {
		int totalChars = 0;
		foreach (ChatMessageContent msg in history)
			totalChars += MeasureContentChars(msg);
		return totalChars / 4 + EstimateToolDefTokens(kernel);
	}

	private static int EstimateToolDefTokens(Kernel? kernel) {
		if (kernel is null) return 0;
		JsonArray tools = ChatHistorySerializer.BuildToolsArray(kernel);
		return tools.ToJsonString().Length / 4;
	}

	private static string FormatTokens(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";

	/// <summary>
	/// Walks messages (newest-first) accumulating token costs.
	/// Returns the ID of the first message that exceeds the keep budget — everything
	/// at or before this ID should be compacted. Messages without API token counts
	/// are estimated from content via local BPE tokenizer.
	/// </summary>
	public static long CalculateSplitPoint(IReadOnlyList<MessageEntity> messagesNewestFirst, int keepRecentTokens) {
		if (messagesNewestFirst.Count == 0) return 0;

		long splitId = messagesNewestFirst[^1].Id;
		int accumulated = 0;
		foreach (MessageEntity m in messagesNewestFirst) {
			accumulated += m.OutputTokens ?? EstimateFromContent(m);
			if (accumulated > keepRecentTokens) {
				splitId = m.Id;
				break;
			}
		}
		return splitId;
	}

	/// <summary>
	/// Estimates token count from message content for messages without API token counts
	/// (tool results, user messages). Uses local BPE tokenizer for accuracy.
	/// </summary>
	internal static int EstimateFromContent(MessageEntity m) {
		string? text = m.SkContent ?? m.Body;
		return text is { Length: > 0 } ? TokenEstimator.Count(text) : 1;
	}
}
