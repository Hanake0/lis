using System.Text;

using Lis.Core.Configuration;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lis.Agent;

public sealed class PromptComposer(
	IOptions<LisOptions> lisOptions,
	ILogger<PromptComposer> logger) {

	private const int MAX_SECTION_CHARS = 20_000;
	private const int MAX_MEMORY_CHARS  = 5_000;

	public async Task<string> BuildAsync(LisDbContext db, long agentId, CancellationToken ct) {
		StringBuilder sb = new();

		List<PromptSectionEntity> sections = await db.PromptSections
			.Where(s => s.IsEnabled && s.AgentId == agentId)
			.OrderBy(s => s.SortOrder)
			.ToListAsync(ct);

		foreach (PromptSectionEntity section in sections) {
			string content = section.Content;

			if (string.IsNullOrWhiteSpace(content)) continue;

			content = this.Interpolate(content);

			if (content.Length > MAX_SECTION_CHARS) {
				content = content[..MAX_SECTION_CHARS];
				logger.LogWarning("Prompt section {Section} truncated to {Max} chars", section.Name, MAX_SECTION_CHARS);
			}

			if (sb.Length > 0) sb.Append("\n\n");
			sb.Append(content);
		}

		List<MemoryEntity> memories = await db.Memories
			.Include(m => m.Contact)
			.OrderByDescending(m => m.CreatedAt)
			.ToListAsync(ct);

		if (memories.Count > 0) {
			StringBuilder memorySb = new("\n\nMemories:");
			int totalLen = 0;

			foreach (MemoryEntity mem in memories) {
				string line = mem.Contact is not null
					? $"\n- [{mem.Contact.Name}] {mem.Content}"
					: $"\n- {mem.Content}";

				if (totalLen + line.Length > MAX_MEMORY_CHARS) break;

				memorySb.Append(line);
				totalLen += line.Length;
			}

			sb.Append(memorySb);
		}

		return sb.ToString();
	}

	private string Interpolate(string content) {
		if (!content.Contains("{{datetime}}")) return content;

		TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(lisOptions.Value.Timezone);
		DateTime local  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

		string timeOfDay = local.Hour switch {
			< 6  => "night",
			< 12 => "morning",
			< 18 => "afternoon",
			_    => "evening",
		};

		string datetime = $"{local:yyyy-MM-dd}, {local:dddd}, {timeOfDay}";

		return content.Replace("{{datetime}}", datetime);
	}
}
