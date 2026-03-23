using System.ComponentModel;
using System.Text;

using Cronos;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class CronPlugin(IServiceScopeFactory scopeFactory) {

	[KernelFunction("cron_add")]
	[Description("Create a new cron job. The cron expression uses standard 5-field format (minute hour day month weekday).")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> CronAddAsync(
		[Description("Cron expression (5 fields: minute hour day month weekday). Example: '*/5 * * * *' for every 5 minutes.")] string cronExpression,
		[Description("Handler identifier — what to run when the job triggers.")] string handler,
		[Description("Human-readable name for this job.")] string name,
		[Description("Whether the job is deterministic (true) or needs AI decision (false). Default: true.")] bool isDeterministic = true) {
		await ToolContext.NotifyAsync($"⏰ Creating cron job: {name}\nExpression: {cronExpression}\nHandler: {handler}");

		CronExpression cron;
		try {
			cron = CronExpression.Parse(cronExpression);
		} catch (Exception ex) {
			return $"Invalid cron expression '{cronExpression}': {ex.Message}";
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId
			?? throw new InvalidOperationException("No chat context");

		ChatEntity chat = await db.Chats.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId)
			?? throw new ArgumentException($"Chat '{chatExternalId}' not found.");

		DateTimeOffset now = DateTimeOffset.UtcNow;
		DateTimeOffset? nextRun = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

		CronJobEntity job = new() {
			Name = name.Trim(),
			CronExpression = cronExpression.Trim(),
			Handler = handler.Trim(),
			ChatId = chat.Id,
			IsDeterministic = isDeterministic,
			Enabled = true,
			NextRunAt = nextRun ?? now.AddDays(1),
			CreatedAt = now,
			UpdatedAt = now
		};

		db.CronJobs.Add(job);
		await db.SaveChangesAsync();

		return $"✅ Cron job #{job.Id} '{job.Name}' created. Next run: {job.NextRunAt:yyyy-MM-dd HH:mm} UTC.";
	}

	[KernelFunction("cron_list")]
	[Description("List all cron jobs for the current chat.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> CronListAsync() {
		await ToolContext.NotifyAsync("📋 Listing cron jobs");

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId
			?? throw new InvalidOperationException("No chat context");

		ChatEntity chat = await db.Chats.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId)
			?? throw new ArgumentException($"Chat '{chatExternalId}' not found.");

		List<CronJobEntity> jobs = await db.CronJobs
			.Where(j => j.ChatId == chat.Id)
			.OrderBy(j => j.Id)
			.ToListAsync();

		if (jobs.Count == 0)
			return "No cron jobs found for this chat.";

		StringBuilder sb = new();
		sb.AppendLine("📋 Cron jobs:");
		foreach (CronJobEntity job in jobs) {
			string status = job.Enabled ? "✅" : "⏸️";
			string lastRun = job.LastRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "never";
			sb.AppendLine($"{status} #{job.Id} | {job.Name} | `{job.CronExpression}` | handler: {job.Handler} | next: {job.NextRunAt:yyyy-MM-dd HH:mm} UTC | last: {lastRun}");
		}

		return sb.ToString().TrimEnd();
	}

	[KernelFunction("cron_remove")]
	[Description("Remove a cron job by its ID.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> CronRemoveAsync(
		[Description("The cron job ID to remove.")] long id) {
		await ToolContext.NotifyAsync($"🗑️ Removing cron job #{id}");

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		CronJobEntity? job = await db.CronJobs.FindAsync(id);
		if (job is null)
			return $"Cron job #{id} not found.";

		string name = job.Name;
		db.CronJobs.Remove(job);
		await db.SaveChangesAsync();

		return $"✅ Cron job #{id} '{name}' removed.";
	}
}
