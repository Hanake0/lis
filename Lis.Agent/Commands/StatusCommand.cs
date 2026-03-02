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

		// Session tokens
		if (ctx.Session is not null) {
			string inputStr  = FormatTokens(ctx.Session.TotalInputTokens);
			string outputStr = FormatTokens(ctx.Session.TotalOutputTokens);
			sb.AppendLine($"🧮 Session tokens: {inputStr} in / {outputStr} out");

			// Cache stats
			long totalCacheInput = ctx.Session.TotalCacheReadTokens + ctx.Session.TotalCacheCreationTokens + ctx.Session.TotalInputTokens;
			if (totalCacheInput > 0) {
				int cacheHitPct = (int)(ctx.Session.TotalCacheReadTokens * 100 / totalCacheInput);
				sb.AppendLine($"🗄️ Cache: {cacheHitPct}% hit · {FormatTokens(ctx.Session.TotalCacheReadTokens)} cached · {FormatTokens(ctx.Session.TotalCacheCreationTokens)} new");
			}

			// Context
			sb.AppendLine($"📚 Context budget: {FormatTokens(modelSettings.ContextBudget)}");

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
