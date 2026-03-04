using System.Text;

using Lis.Core.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Lis.Agent.Commands;

public sealed class StatusCommand(ModelSettings modelSettings) : IChatCommand {
	public string[] Triggers => ["/status"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		StringBuilder sb = new();

		sb.AppendLine("🤖 Lis");

		// Model + thinking
		string thinkingLabel = modelSettings.ThinkingEffort switch {
			"low"    => " · Think: low",
			"medium" => " · Think: medium",
			"high"   => " · Think: high",
			{ Length: > 0 } t => $" · Think: {t}",
			_        => ""
		};
		sb.AppendLine($"🧠 Model: {modelSettings.Model}{thinkingLabel}");

		if (ctx.Session is not null) {
			// Last API response tokens (from latest message with usage data)
			var lastUsage = await ctx.Db.Messages
				.Where(m => m.ChatId == ctx.Chat.Id && m.InputTokens != null)
				.OrderByDescending(m => m.Id)
				.Select(m => new { m.InputTokens, m.OutputTokens, m.CacheReadTokens, m.CacheCreationTokens })
				.FirstOrDefaultAsync(ct);

			if (lastUsage is not null) {
				sb.AppendLine($"🧮 Tokens: {FormatTokens(lastUsage.InputTokens ?? 0)} in / {FormatTokens(lastUsage.OutputTokens ?? 0)} out");

				long cacheRead     = lastUsage.CacheReadTokens ?? 0;
				long cacheCreation = lastUsage.CacheCreationTokens ?? 0;
				long totalForHit = cacheRead + cacheCreation + (lastUsage.InputTokens ?? 0);
				int hitPct = totalForHit > 0 ? (int)(cacheRead * 100 / totalForHit) : 0;
				sb.AppendLine($"🗄️ Cache: {hitPct}% hit · {FormatTokens(cacheRead)} cached · {FormatTokens(cacheCreation)} new");
			}

			// Context usage
			long contextTokens = ctx.Session.ContextTokens;
			int budget = modelSettings.ContextBudget;
			int compactions = await ctx.Db.Sessions
				.Where(s => s.ChatId == ctx.Chat.Id && s.EndMessageId != null)
				.CountAsync(ct);
			if (contextTokens > 0) {
				int pct = budget > 0 ? (int)(contextTokens * 100 / budget) : 0;
				string compactStr = compactions > 0 ? $" · 🧹 Compactions: {compactions}" : "";
				sb.AppendLine($"📚 Context: {FormatTokens(contextTokens)}/{FormatTokens(budget)} ({pct}%){compactStr}");
			} else {
				sb.AppendLine($"📚 Context budget: {FormatTokens(budget)}");
			}

			// Session info
			int messageCount = await ctx.Db.Messages
				.Where(m => m.ChatId == ctx.Chat.Id
				         && (ctx.Session.StartMessageId == null || m.Id >= ctx.Session.StartMessageId))
				.CountAsync(ct);

			string elapsed = FormatElapsed(DateTimeOffset.UtcNow - ctx.Session.CreatedAt);
			sb.Append($"🧵 Session: #{ctx.Session.Id} · started {elapsed} ago · {messageCount} messages");
		} else {
			sb.Append("🧵 No active session");
		}

		return sb.ToString();
	}

	private static string FormatTokens(long tokens) {
		return tokens switch {
			>= 1_000_000 => $"{tokens / 1_000_000.0:F1}M",
			>= 1_000     => $"{tokens / 1_000.0:F1}k",
			_            => $"{tokens}"
		};
	}

	private static string FormatElapsed(TimeSpan elapsed) {
		if (elapsed.TotalDays >= 1) return $"{(int)elapsed.TotalDays}d";
		if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}h";
		if (elapsed.TotalMinutes >= 1) return $"{(int)elapsed.TotalMinutes}m";
		return $"{(int)elapsed.TotalSeconds}s";
	}
}
