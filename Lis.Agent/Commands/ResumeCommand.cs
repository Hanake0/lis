using System.Text;

using Lis.Core.Configuration;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Lis.Agent.Commands;

public sealed class ResumeCommand(
	CompactionService                            compactionService,
	ModelSettings                                modelSettings,
	IOptions<LisOptions>                         lisOptions,
	IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null) : IChatCommand {

	public string[] Triggers => ["/resume"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Args is null or { Length: 0 })
			return await this.ListSessionsAsync(ctx, ct);

		if (long.TryParse(ctx.Args, out long id))
			return await this.ResumeByIdAsync(ctx, id, ct);

		return await this.SearchSessionsAsync(ctx, ctx.Args, ct);
	}

	private async Task<string> ResumeByIdAsync(CommandContext ctx, long targetId, CancellationToken ct) {
		if (ctx.Session is not null && ctx.Session.Id == targetId)
			return "You're already in this session.";

		SessionEntity? target = await ctx.Db.Sessions
			.FirstOrDefaultAsync(s => s.Id == targetId && s.ChatId == ctx.Chat.Id, ct);

		if (target is null)
			return $"Session #{targetId} not found.";

		// Token check — can we fit the full session?
		int resumeBudget = lisOptions.Value.ResumeTokenBudget > 0
			? lisOptions.Value.ResumeTokenBudget
			: (int)(modelSettings.ContextBudget * 0.7);

		long estimatedTokens = await ctx.Db.Messages
			.Where(m => m.SessionId == target.Id)
			.SumAsync(m => (long)(m.OutputTokens ?? m.InputTokens ?? 0), ct);

		if (estimatedTokens <= resumeBudget) {
			// Full resume: finalize current, reopen target
			if (ctx.Session is not null) {
				long oldSessionId = ctx.Session.Id;
				_ = Task.Run(async () => {
					try {
						await compactionService.GenerateSessionSummaryAsync(oldSessionId, CancellationToken.None);
					} catch {
						// Best effort
					}
				}, CancellationToken.None);
			}

			target.Summary          = null;
			target.SummaryEmbedding = null;
			ctx.Chat.CurrentSessionId = target.Id;
			await ctx.Db.SaveChangesAsync(ct);

			int compactionThreshold = this.ResolveCompactionThreshold();
			int pct = modelSettings.ContextBudget > 0
				? (int)(estimatedTokens * 100 / modelSettings.ContextBudget) : 0;
			return $"🔄 Resumed session #{target.Id} with full context."
			     + $"\n  📊 Context: ~{Fmt(estimatedTokens)}/{Fmt(modelSettings.ContextBudget)} ({pct}%) · Compaction at {Fmt(compactionThreshold)}";
		}

		// Summary fallback
		SessionEntity newSession = await compactionService.StartNewSessionAsync(
			ctx.Chat, ctx.Session, isExplicitBreak: true, ctx.Db, ct);
		newSession.ParentSessionId = target.Id;
		await ctx.Db.SaveChangesAsync(ct);

		string summaryPreview = target.Summary is { Length: > 0 }
			? Truncate(target.Summary, 120)
			: "No summary available — context may be limited.";

		return $"⚠️ Session #{target.Id} context too large (~{Fmt(estimatedTokens)}), resuming from summary."
		     + $"\n📝 {summaryPreview}";
	}

	private async Task<string> ListSessionsAsync(CommandContext ctx, CancellationToken ct) {
		List<SessionEntity> sessions = await ctx.Db.Sessions
			.Where(s => s.ChatId == ctx.Chat.Id
			         && s.Id != ctx.Chat.CurrentSessionId
			         && s.Summary != null)
			.OrderByDescending(s => s.UpdatedAt)
			.Take(5)
			.ToListAsync(ct);

		if (sessions.Count == 0)
			return "No previous sessions to resume.";

		StringBuilder sb = new();
		sb.AppendLine("📋 Recent sessions:");
		sb.AppendLine();

		foreach (SessionEntity s in sessions) {
			string elapsed = FormatElapsed(DateTimeOffset.UtcNow - s.UpdatedAt);
			string preview = Truncate(s.Summary!, 60);
			sb.AppendLine($"#{s.Id} · {elapsed} ago — {preview}");
		}

		sb.AppendLine();
		sb.Append("💡 /resume <id> or /resume <search text>");

		return sb.ToString();
	}

	private async Task<string> SearchSessionsAsync(CommandContext ctx, string query, CancellationToken ct) {
		List<SessionEntity> results;

		if (embeddingGenerator is not null) {
			// Semantic search via cosine distance
			GeneratedEmbeddings<Embedding<float>> embedResult =
				await embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
			Vector queryVector = new(embedResult[0].Vector);

			results = await ctx.Db.Sessions
				.Where(s => s.ChatId == ctx.Chat.Id
				         && s.SummaryEmbedding != null
				         && s.Id != ctx.Chat.CurrentSessionId)
				.OrderBy(s => s.SummaryEmbedding!.CosineDistance(queryVector))
				.Take(5)
				.ToListAsync(ct);
		} else {
			// Fallback: text search
			results = await ctx.Db.Sessions
				.Where(s => s.ChatId == ctx.Chat.Id
				         && s.Summary != null
				         && s.Id != ctx.Chat.CurrentSessionId
				         && EF.Functions.ILike(s.Summary!, $"%{query}%"))
				.OrderByDescending(s => s.UpdatedAt)
				.Take(5)
				.ToListAsync(ct);
		}

		if (results.Count == 0)
			return $"No sessions found matching \"{query}\".";

		StringBuilder sb = new();
		sb.AppendLine($"🔍 Sessions matching \"{query}\":");
		sb.AppendLine();

		foreach (SessionEntity s in results) {
			string elapsed = FormatElapsed(DateTimeOffset.UtcNow - s.UpdatedAt);
			string preview = s.Summary is { Length: > 0 } ? Truncate(s.Summary, 60) : "(no summary)";
			sb.AppendLine($"#{s.Id} · {elapsed} ago — {preview}");
		}

		sb.AppendLine();
		sb.Append("💡 /resume <id> to continue");

		return sb.ToString();
	}

	private int ResolveCompactionThreshold() =>
		lisOptions.Value.CompactionThreshold > 0
			? lisOptions.Value.CompactionThreshold
			: (int)(modelSettings.ContextBudget * 0.8);

	private static string Truncate(string text, int maxLen) =>
		text.Length <= maxLen ? text : text[..maxLen] + "...";

	private static string Fmt(long tokens) =>
		tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : $"{tokens}";

	private static string FormatElapsed(TimeSpan elapsed) {
		if (elapsed.TotalDays >= 1) return $"{(int)elapsed.TotalDays}d";
		if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}h";
		if (elapsed.TotalMinutes >= 1) return $"{(int)elapsed.TotalMinutes}m";
		return $"{(int)elapsed.TotalSeconds}s";
	}
}
