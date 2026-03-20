using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lis.Agent;

public sealed class AgentService(
	ILogger<AgentService>     logger) {

	[Trace("AgentService > ResolveForChatAsync")]
	public async Task<AgentEntity> ResolveForChatAsync(LisDbContext db, ChatEntity chat, CancellationToken ct) {
		if (chat.AgentId is not null) {
			AgentEntity? agent = chat.Agent ?? await db.Agents.FindAsync([chat.AgentId], ct);
			if (agent is not null) return agent;
		}

		// Fallback to default agent
		AgentEntity? defaultAgent = await db.Agents.FirstOrDefaultAsync(a => a.IsDefault, ct);
		if (defaultAgent is null) {
			logger.LogError("No default agent found");
			throw new InvalidOperationException("No default agent configured");
		}

		// Assign default agent to chat
		chat.AgentId = defaultAgent.Id;
		chat.Agent   = defaultAgent;
		await db.SaveChangesAsync(ct);

		return defaultAgent;
	}

	/// <summary>
	/// Runs all mention detection strategies, setting <see cref="IncomingMessage.IsBotMentioned"/>.
	/// New detection strategies should be added here — this is the single source of truth.
	/// </summary>
	public async Task DetectMentionAsync(LisDbContext db, ChatEntity chat, IncomingMessage message, CancellationToken ct) {
		if (!message.IsGroup) return;
		if (message.IsBotMentioned) return;

		// Strategy 1: reply-to-bot — user replied to a message the bot sent
		if (message.RepliedId is { Length: > 0 } repliedId) {
			bool repliedToBot = await db.Messages
				.AnyAsync(m => m.ExternalId == repliedId && m.IsFromMe, ct);
			if (repliedToBot) { message.IsBotMentioned = true; return; }
		}

		// Strategy 2: text mention — message body contains the bot's display name
		if (message.Body is not { Length: > 0 } body) return;

		AgentEntity agent = await this.ResolveForChatAsync(db, chat, ct);
		if (agent.DisplayName is { Length: > 0 } botName
		    && body.Contains(botName, StringComparison.OrdinalIgnoreCase))
			message.IsBotMentioned = true;
	}

	/// <summary>
	/// Full async auth flow: mention detection + gate check.
	/// Single entry point for all callers — prevents forgetting mention detection.
	/// </summary>
	public async Task<bool> ShouldRespondAsync(
		LisDbContext db, ChatEntity chat, IncomingMessage message, string ownerJid, CancellationToken ct) {
		await this.DetectMentionAsync(db, chat, message, ct);
		return this.ShouldRespond(chat, message, ownerJid);
	}

	internal bool ShouldRespond(ChatEntity chat, IncomingMessage message, string ownerJid) {
		if (!chat.Enabled) return false;

		// Owner always bypasses all gates
		if (!string.IsNullOrEmpty(ownerJid) && message.SenderId == ownerJid) return true;

		// Sender authorized? AllowedSenders OR OpenGroup (for groups)
		bool authorized = chat.AllowedSenders.Any(s => s.SenderId == message.SenderId)
		                  || (message.IsGroup && chat.OpenGroup);

		if (!authorized) return false;

		// Mention gate: groups with RequireMention need the bot to be mentioned
		if (message.IsGroup && chat.RequireMention && !message.IsBotMentioned) return false;

		return true;
	}

	public static ModelSettings ToModelSettings(AgentEntity agent) => new() {
		Model          = agent.Model,
		MaxTokens      = agent.MaxTokens,
		ContextBudget  = agent.ContextBudget,
		ThinkingEffort = agent.ThinkingEffort
	};

	[Trace("AgentService > SeedDefaultAsync")]
	public async Task SeedDefaultAsync(LisDbContext db, ModelSettings envDefaults, LisOptions envOptions, CancellationToken ct) {
		AgentEntity? existing = await db.Agents.FirstOrDefaultAsync(a => a.IsDefault, ct);

		if (existing is not null) {
			// Sync model from env if agent's model is still empty
			if (string.IsNullOrEmpty(existing.Model) && !string.IsNullOrEmpty(envDefaults.Model)) {
				existing.Model         = envDefaults.Model;
				existing.MaxTokens     = envDefaults.MaxTokens;
				existing.ContextBudget = envDefaults.ContextBudget;
				existing.ThinkingEffort = envDefaults.ThinkingEffort;
				existing.UpdatedAt     = DateTimeOffset.UtcNow;
				await db.SaveChangesAsync(ct);
				if (logger.IsEnabled(LogLevel.Information))
				logger.LogInformation("Synced default agent model from env: {Model}", envDefaults.Model);
			}
			return;
		}

		AgentEntity agent = new() {
			Name                    = "default",
			DisplayName             = "Lis",
			Model                   = envDefaults.Model,
			MaxTokens               = envDefaults.MaxTokens,
			ContextBudget           = envDefaults.ContextBudget,
			ThinkingEffort          = envDefaults.ThinkingEffort,
			ToolNotifications       = envOptions.ToolNotifications,
			CompactionThreshold     = envOptions.CompactionThreshold,
			KeepRecentTokens        = envOptions.KeepRecentTokens,
			ToolPruneThreshold      = envOptions.ToolPruneThreshold,
			ToolKeepThreshold       = envOptions.ToolKeepThreshold,
			ToolSummarizationPolicy = envOptions.ToolSummarizationPolicy,
			IsDefault               = true,
			CreatedAt               = DateTimeOffset.UtcNow,
			UpdatedAt               = DateTimeOffset.UtcNow
		};

		db.Agents.Add(agent);
		await db.SaveChangesAsync(ct);

		// Reassign any orphan prompt sections (agent_id == 0 from before agents existed)
		int orphans = await db.PromptSections
			.Where(s => s.AgentId == 0)
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.AgentId, agent.Id), ct);
		if (orphans > 0 && logger.IsEnabled(LogLevel.Information))
			logger.LogInformation("Reassigned {Count} orphan prompt sections to default agent", orphans);
		if (logger.IsEnabled(LogLevel.Information))
			logger.LogInformation("Seeded default agent with model {Model}", agent.Model);
	}
}
