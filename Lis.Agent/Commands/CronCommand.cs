using System.Text;
using System.Text.RegularExpressions;

using Cronos;

using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Agent.Commands;

public sealed partial class CronCommand : IChatCommand {
	public string[] Triggers => ["/cron"];
	public bool OwnerOnly => true;

	private const string Usage =
		"""
		Usage:
		  /cron add "<cron_expr>" <handler> <name>
		  /cron list
		  /cron remove <id>

		Examples:
		  /cron add "*/5 * * * *" daily_summary My Summary Job
		  /cron add "0 9 * * 1" weekly_report Weekly Report
		  /cron list
		  /cron remove 42
		""";

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(ctx.Args))
			return Usage;

		string args = ctx.Args.Trim();
		string subcommand = args.Split(' ', 2)[0].ToLowerInvariant();

		return subcommand switch {
			"add"    => await this.HandleAddAsync(ctx, args[3..].TrimStart(), ct),
			"list"   => await this.HandleListAsync(ctx, ct),
			"remove" => await this.HandleRemoveAsync(ctx, args[6..].TrimStart(), ct),
			_        => Usage
		};
	}

	private async Task<string> HandleAddAsync(CommandContext ctx, string args, CancellationToken ct) {
		// Parse: "<cron_expr>" <handler> <name...>
		Match match = AddPattern().Match(args);
		if (!match.Success)
			return $"Usage: /cron add \"<cron_expr>\" <handler> <name>\n\nExample: /cron add \"0 9 * * *\" daily_summary Morning Summary";

		string cronExpr = match.Groups["cron"].Value;
		string handler = match.Groups["handler"].Value;
		string name = match.Groups["name"].Value.Trim();

		if (string.IsNullOrWhiteSpace(name))
			return "Usage: /cron add \"<cron_expr>\" <handler> <name>";

		CronExpression cron;
		try {
			cron = CronExpression.Parse(cronExpr);
		} catch (Exception ex) {
			return $"❌ Invalid cron expression '{cronExpr}': {ex.Message}";
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		DateTimeOffset? nextRun = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

		CronJobEntity job = new() {
			Name = name,
			CronExpression = cronExpr,
			Handler = handler,
			ChatId = ctx.Chat.Id,
			Enabled = true,
			IsDeterministic = true,
			NextRunAt = nextRun ?? now.AddDays(1),
			CreatedAt = now,
			UpdatedAt = now
		};

		ctx.Db.CronJobs.Add(job);
		await ctx.Db.SaveChangesAsync(ct);

		return $"✅ Cron job #{job.Id} '{job.Name}' created.\nExpression: {job.CronExpression}\nHandler: {job.Handler}\nNext run: {job.NextRunAt:yyyy-MM-dd HH:mm} UTC";
	}

	private async Task<string> HandleListAsync(CommandContext ctx, CancellationToken ct) {
		List<CronJobEntity> jobs = await ctx.Db.CronJobs
			.Where(j => j.ChatId == ctx.Chat.Id)
			.OrderBy(j => j.Id)
			.ToListAsync(ct);

		if (jobs.Count == 0)
			return "No cron jobs found for this chat.";

		StringBuilder sb = new();
		sb.AppendLine("📋 Cron jobs:");
		foreach (CronJobEntity job in jobs) {
			string status = job.Enabled ? "✅" : "⏸️";
			string lastRun = job.LastRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "never";
			sb.AppendLine($"{status} #{job.Id} | {job.Name} | `{job.CronExpression}` | {job.Handler} | next: {job.NextRunAt:yyyy-MM-dd HH:mm} UTC | last: {lastRun}");
		}

		return sb.ToString().TrimEnd();
	}

	private async Task<string> HandleRemoveAsync(CommandContext ctx, string args, CancellationToken ct) {
		if (!long.TryParse(args.Trim(), out long id))
			return "❌ Invalid job ID. Usage: /cron remove <id>";

		CronJobEntity? job = await ctx.Db.CronJobs
			.FirstOrDefaultAsync(j => j.Id == id && j.ChatId == ctx.Chat.Id, ct);

		if (job is null)
			return $"❌ Cron job #{id} not found in this chat.";

		string name = job.Name;
		ctx.Db.CronJobs.Remove(job);
		await ctx.Db.SaveChangesAsync(ct);

		return $"✅ Cron job #{id} '{name}' removed.";
	}

	[GeneratedRegex("""^"(?<cron>[^"]+)"\s+(?<handler>\S+)\s+(?<name>.+)$""")]
	private static partial Regex AddPattern();
}
