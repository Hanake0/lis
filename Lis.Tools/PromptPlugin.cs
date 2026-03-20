using System.ComponentModel;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class PromptPlugin(IServiceScopeFactory scopeFactory) {
	[KernelFunction("list_prompt_sections")]
	[Description("List prompt sections. Use type='names' for a summary or type='full' for complete content of all sections.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.Open)]
	public async Task<string> ListPromptSectionsAsync(
		[Description("Listing type: 'names' for summary, 'full' for complete content")]
		string type = "names") {
		await ToolContext.NotifyAsync($"📋 Listing prompt sections\ntype: {type}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		List<PromptSectionEntity> sections = await db.PromptSections
													 .Where(s => s.AgentId == agentId)
													 .OrderBy(s => s.SortOrder)
													 .ToListAsync();

		if (sections.Count == 0) return "No prompt sections found.";

		StringBuilder sb = new();

		foreach (PromptSectionEntity section in sections) {
			string status = section.IsEnabled ? "enabled" : "disabled";

			if (type == "full") {
				sb.AppendLine($"--- {section.Name} (order: {section.SortOrder}, {status}) ---");
				sb.AppendLine(section.Content);
				sb.AppendLine();
			} else {
				sb.AppendLine($"- {section.Name} (order: {section.SortOrder}, {status})");
			}
		}

		return sb.ToString().TrimEnd();
	}

	[KernelFunction("get_prompt_section")]
	[Description("Get the full content of a specific prompt section by name.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.Open)]
	public async Task<string> GetPromptSectionAsync(
		[Description("Section name (e.g. 'soul', 'user', 'instructions')")]
		string name) {
		await ToolContext.NotifyAsync($"📄 Reading prompt section\nname: {name}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		PromptSectionEntity? section = await db.PromptSections
											   .FirstOrDefaultAsync(s => s.AgentId == agentId && s.Name == name);

		if (section is null) return $"Section '{name}' not found.";

		return section.Content;
	}

	[KernelFunction("update_prompt_section")]
	[Description("Update the content of a prompt section. Changes take effect on the next message.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.Open)]
	public async Task<string> UpdatePromptSectionAsync(
		[Description("Section name (e.g. 'soul', 'user', 'instructions')")]
		string name,
		[Description("New content for the section")]
		string content) {
		await ToolContext.NotifyAsync($"✏️ Updating prompt section\nname: {name}\n```\n{content}\n```");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		PromptSectionEntity? section = await db.PromptSections
											   .FirstOrDefaultAsync(s => s.AgentId == agentId && s.Name == name);

		if (section is null) return $"Section '{name}' not found.";

		section.Content   = content;
		section.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync();

		return $"Section '{name}' updated.";
	}
}
