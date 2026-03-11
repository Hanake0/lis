using System.ComponentModel;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class ConfigPlugin(IServiceScopeFactory scopeFactory) {

	private static readonly HashSet<string> KnownAgentFields = [
		"model", "max_tokens", "context_budget", "thinking_effort",
		"tool_notifications", "compaction_threshold", "keep_recent_tokens",
		"tool_prune_threshold", "tool_keep_threshold", "tool_summarization_policy",
		"display_name", "group_context_prompt"
	];

	[KernelFunction("get_agent_config")]
	[Description("Read the current agent's configuration fields.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> GetAgentConfigAsync() {
		await ToolContext.NotifyAsync("⚙️ Reading agent config");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		AgentEntity? agent = await db.Agents.FindAsync(agentId);
		if (agent is null) return "Agent not found.";

		StringBuilder sb = new();
		sb.AppendLine($"name: {agent.Name}");
		sb.AppendLine($"display_name: {agent.DisplayName ?? "(none)"}");
		sb.AppendLine($"model: {agent.Model}");
		sb.AppendLine($"max_tokens: {agent.MaxTokens}");
		sb.AppendLine($"context_budget: {agent.ContextBudget}");
		sb.AppendLine($"thinking_effort: {agent.ThinkingEffort ?? "(none)"}");
		sb.AppendLine($"tool_notifications: {agent.ToolNotifications}");
		sb.AppendLine($"compaction_threshold: {agent.CompactionThreshold}");
		sb.AppendLine($"keep_recent_tokens: {agent.KeepRecentTokens}");
		sb.AppendLine($"tool_prune_threshold: {agent.ToolPruneThreshold}");
		sb.AppendLine($"tool_keep_threshold: {agent.ToolKeepThreshold}");
		sb.AppendLine($"tool_summarization_policy: {agent.ToolSummarizationPolicy ?? "(none)"}");
		sb.AppendLine($"group_context_prompt: {agent.GroupContextPrompt ?? "(default)"}");
		sb.Append($"is_default: {agent.IsDefault}");

		return sb.ToString();
	}

	[KernelFunction("update_agent_config")]
	[Description("Update a configuration field on the current agent. Valid keys: model, max_tokens, context_budget, thinking_effort, tool_notifications, compaction_threshold, keep_recent_tokens, tool_prune_threshold, tool_keep_threshold, tool_summarization_policy, display_name.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> UpdateAgentConfigAsync(
		[Description("Configuration key to update")] string key,
		[Description("New value")] string value) {
		await ToolContext.NotifyAsync($"✏️ Updating agent config\n{key} = {value}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		AgentEntity? agent = await db.Agents.FindAsync(agentId);
		if (agent is null) return "Agent not found.";

		if (!KnownAgentFields.Contains(key))
			return $"Unknown config key '{key}'. Valid keys: {string.Join(", ", KnownAgentFields)}.";

		switch (key) {
			case "model":
				agent.Model = value;
				break;
			case "max_tokens":
				if (!int.TryParse(value, out int maxTokens)) return "Invalid integer value for max_tokens.";
				agent.MaxTokens = maxTokens;
				break;
			case "context_budget":
				if (!int.TryParse(value, out int contextBudget)) return "Invalid integer value for context_budget.";
				agent.ContextBudget = contextBudget;
				break;
			case "thinking_effort":
				agent.ThinkingEffort = string.IsNullOrWhiteSpace(value) ? null : value;
				break;
			case "tool_notifications":
				if (!bool.TryParse(value, out bool toolNotif)) return "Invalid boolean value for tool_notifications.";
				agent.ToolNotifications = toolNotif;
				break;
			case "compaction_threshold":
				if (!int.TryParse(value, out int compThreshold)) return "Invalid integer value for compaction_threshold.";
				agent.CompactionThreshold = compThreshold;
				break;
			case "keep_recent_tokens":
				if (!int.TryParse(value, out int keepRecent)) return "Invalid integer value for keep_recent_tokens.";
				agent.KeepRecentTokens = keepRecent;
				break;
			case "tool_prune_threshold":
				if (!int.TryParse(value, out int toolPrune)) return "Invalid integer value for tool_prune_threshold.";
				agent.ToolPruneThreshold = toolPrune;
				break;
			case "tool_keep_threshold":
				if (!int.TryParse(value, out int toolKeep)) return "Invalid integer value for tool_keep_threshold.";
				agent.ToolKeepThreshold = toolKeep;
				break;
			case "tool_summarization_policy":
				agent.ToolSummarizationPolicy = string.IsNullOrWhiteSpace(value) ? null : value;
				break;
			case "display_name":
				agent.DisplayName = string.IsNullOrWhiteSpace(value) ? null : value;
				break;
			case "group_context_prompt":
				agent.GroupContextPrompt = string.IsNullOrWhiteSpace(value) ? null : value;
				break;
		}

		agent.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync();

		return $"Agent config '{key}' updated to '{value}'.";
	}

	[KernelFunction("get_chat_config")]
	[Description("Read the current chat's configuration fields.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> GetChatConfigAsync() {
		await ToolContext.NotifyAsync("⚙️ Reading chat config");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId ?? throw new InvalidOperationException("No chat context");
		ChatEntity? chat = await db.Chats
			.Include(c => c.Agent)
			.Include(c => c.AllowedSenders)
			.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId);

		if (chat is null) return "Chat not found.";

		StringBuilder sb = new();
		sb.AppendLine($"enabled: {chat.Enabled}");
		sb.AppendLine($"require_mention: {chat.RequireMention}");
		sb.AppendLine($"open_group: {chat.OpenGroup}");
		sb.AppendLine($"group_context_messages: {chat.GroupContextMessages?.ToString() ?? "(default)"}");
		sb.AppendLine($"agent: {chat.Agent?.Name ?? "(none)"}");
		string senders = chat.AllowedSenders.Count > 0
			? string.Join(", ", chat.AllowedSenders.Select(s => s.SenderId))
			: "(none)";
		sb.Append($"allowed_senders: {senders}");

		return sb.ToString();
	}

	[KernelFunction("update_chat_config")]
	[Description("Update a configuration field on the current chat. Valid keys: enabled (bool), require_mention (bool), open_group (bool), group_context_messages (int).")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> UpdateChatConfigAsync(
		[Description("Configuration key to update (enabled, require_mention, open_group, group_context_messages)")] string key,
		[Description("New value")] string value) {
		await ToolContext.NotifyAsync($"✏️ Updating chat config\n{key} = {value}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId ?? throw new InvalidOperationException("No chat context");
		ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId);

		if (chat is null) return "Chat not found.";

		switch (key) {
			case "enabled":
				if (!bool.TryParse(value, out bool enabled)) return "Invalid boolean value for enabled.";
				chat.Enabled = enabled;
				break;
			case "require_mention":
				if (!bool.TryParse(value, out bool requireMention)) return "Invalid boolean value for require_mention.";
				chat.RequireMention = requireMention;
				break;
			case "open_group":
				if (!bool.TryParse(value, out bool openGroup)) return "Invalid boolean value for open_group.";
				chat.OpenGroup = openGroup;
				break;
			case "group_context_messages":
				if (!int.TryParse(value, out int groupCtx)) return "Invalid integer value for group_context_messages.";
				chat.GroupContextMessages = groupCtx;
				break;
			default:
				return $"Unknown config key '{key}'. Valid keys: enabled, require_mention, open_group, group_context_messages.";
		}

		chat.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync();

		return $"Chat config '{key}' updated to '{value}'.";
	}

	[KernelFunction("add_allowed_sender")]
	[Description("Add a sender ID to the current chat's allowed senders list.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> AddAllowedSenderAsync(
		[Description("The sender ID to allow")] string senderId) {
		await ToolContext.NotifyAsync($"➕ Adding allowed sender: {senderId}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId ?? throw new InvalidOperationException("No chat context");
		ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId);

		if (chat is null) return "Chat not found.";

		bool exists = await db.ChatAllowedSenders
			.AnyAsync(s => s.ChatId == chat.Id && s.SenderId == senderId);

		if (exists) return $"Sender '{senderId}' is already allowed.";

		db.ChatAllowedSenders.Add(new ChatAllowedSenderEntity {
			ChatId   = chat.Id,
			SenderId = senderId
		});
		await db.SaveChangesAsync();

		return $"Sender '{senderId}' added to allowed list.";
	}

	[KernelFunction("remove_allowed_sender")]
	[Description("Remove a sender ID from the current chat's allowed senders list.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> RemoveAllowedSenderAsync(
		[Description("The sender ID to remove")] string senderId) {
		await ToolContext.NotifyAsync($"➖ Removing allowed sender: {senderId}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId ?? throw new InvalidOperationException("No chat context");
		ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId);

		if (chat is null) return "Chat not found.";

		ChatAllowedSenderEntity? sender = await db.ChatAllowedSenders
			.FirstOrDefaultAsync(s => s.ChatId == chat.Id && s.SenderId == senderId);

		if (sender is null) return $"Sender '{senderId}' not found in allowed list.";

		db.ChatAllowedSenders.Remove(sender);
		await db.SaveChangesAsync();

		return $"Sender '{senderId}' removed from allowed list.";
	}

	[KernelFunction("list_allowed_senders")]
	[Description("List all allowed senders for the current chat.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> ListAllowedSendersAsync() {
		await ToolContext.NotifyAsync("📋 Listing allowed senders");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		string chatExternalId = ToolContext.ChatId ?? throw new InvalidOperationException("No chat context");
		ChatEntity? chat = await db.Chats
			.FirstOrDefaultAsync(c => c.ExternalId == chatExternalId);

		if (chat is null) return "Chat not found.";

		List<ChatAllowedSenderEntity> senders = await db.ChatAllowedSenders
			.Where(s => s.ChatId == chat.Id)
			.ToListAsync();

		if (senders.Count == 0) return "No allowed senders configured.";

		StringBuilder sb = new();
		sb.AppendLine("📋 Allowed senders:");
		foreach (ChatAllowedSenderEntity sender in senders) {
			sb.AppendLine($"- {sender.SenderId}");
		}

		return sb.ToString().TrimEnd();
	}
}
