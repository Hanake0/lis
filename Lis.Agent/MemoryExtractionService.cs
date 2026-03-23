using System.Text.Json;
using System.Text.RegularExpressions;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Pgvector;

namespace Lis.Agent;

public interface IMemoryExtractionService {
	Task ExtractAsync(List<string> conversationMessages, CancellationToken ct);
}

public sealed class MemoryExtractionService(
	[FromKeyedServices("extraction")] IChatClient extractionClient,
	IServiceScopeFactory                          scopeFactory,
	ILogger<MemoryExtractionService>              logger) : IMemoryExtractionService {

	private const int MaxMemoriesPerExtraction = 5;

	private const string ExtractionPrompt = """
		Analyze the following conversation and extract 0-5 factual memories worth remembering long-term.
		Focus on: personal preferences, facts about people, decisions made, important dates, commitments.
		Skip: greetings, small talk, transient information, tool call details.

		Return a JSON array of objects with:
		- "content": the fact to remember (concise, standalone sentence)
		- "contact_name": person's name this is about (optional, null if general)

		If nothing worth remembering, return an empty array: []

		Conversation:
		""";

	[Trace("MemoryExtractionService > ExtractAsync")]
	public async Task ExtractAsync(List<string> conversationMessages, CancellationToken ct) {
		try {
			string conversation = string.Join("\n", conversationMessages);
			string prompt = ExtractionPrompt + conversation;

			ChatOptions options = new() { MaxOutputTokens = 512, Temperature = 0.1f };
			ChatResponse response = await extractionClient.GetResponseAsync(
				[new ChatMessage(ChatRole.User, prompt)], options, ct);

			string? text = response.Text?.Trim();
			if (string.IsNullOrWhiteSpace(text)) return;

			// Strip markdown code fences if present
			text = StripCodeFences(text);

			List<ExtractedMemory>? memories;
			try {
				memories = JsonSerializer.Deserialize<List<ExtractedMemory>>(text, JsonOpts);
			} catch (JsonException ex) {
				logger.LogWarning(ex, "Failed to parse extraction response: {Text}", text[..Math.Min(text.Length, 200)]);
				return;
			}

			if (memories is null || memories.Count == 0) return;

			// Cap at max
			if (memories.Count > MaxMemoriesPerExtraction)
				memories = memories.Take(MaxMemoriesPerExtraction).ToList();

			// Filter out empty/null content
			memories = memories.Where(m => !string.IsNullOrWhiteSpace(m.Content)).ToList();
			if (memories.Count == 0) return;

			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
			IEmbeddingGenerator<string, Embedding<float>>? embeddingGen =
				scope.ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();

			foreach (ExtractedMemory mem in memories) {
				long? contactId = await ResolveOrCreateContactAsync(db, mem.ContactName);
				Vector? embedding = await GenerateEmbeddingAsync(embeddingGen, mem.Content!);

				MemoryEntity entity = new() {
					Content        = mem.Content!.Trim(),
					ContactId      = contactId,
					Embedding      = embedding,
					RelevanceScore = 1.0f,
					CreatedAt      = DateTimeOffset.UtcNow,
					UpdatedAt      = DateTimeOffset.UtcNow,
				};

				db.Memories.Add(entity);
			}

			await db.SaveChangesAsync(ct);

			if (logger.IsEnabled(LogLevel.Information))
				logger.LogInformation("Extracted {Count} memories from conversation", memories.Count);
		} catch (Exception ex) {
			logger.LogWarning(ex, "Memory extraction failed");
		}
	}

	/// <summary>
	/// Calculates relevance score based on last access time.
	/// Formula: max(0.1, 1.0 - (days_since_access / 30.0) * 0.5)
	/// </summary>
	public static float CalculateRelevanceScore(DateTimeOffset? lastAccessedAt) {
		if (lastAccessedAt is null) return 1.0f;

		double daysSinceAccess = (DateTimeOffset.UtcNow - lastAccessedAt.Value).TotalDays;
		float score = (float)(1.0 - daysSinceAccess / 30.0 * 0.5);
		return Math.Max(0.1f, score);
	}

	private static string StripCodeFences(string text) {
		// Remove ```json ... ``` or ``` ... ```
		Match match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
		return match.Success ? match.Groups[1].Value.Trim() : text;
	}

	private static async Task<long?> ResolveOrCreateContactAsync(LisDbContext db, string? contactName) {
		if (string.IsNullOrWhiteSpace(contactName)) return null;

		ContactEntity? contact = await db.Contacts
			.FirstOrDefaultAsync(c => c.Name != null
				&& c.Name.Equals(contactName.Trim(), StringComparison.OrdinalIgnoreCase));

		if (contact is not null) return contact.Id;

		contact = new ContactEntity {
			Name      = contactName.Trim(),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};

		db.Contacts.Add(contact);
		await db.SaveChangesAsync();

		return contact.Id;
	}

	private static async Task<Vector?> GenerateEmbeddingAsync(
		IEmbeddingGenerator<string, Embedding<float>>? embeddingGen, string content) {
		if (embeddingGen is null) return null;

		GeneratedEmbeddings<Embedding<float>> result = await embeddingGen.GenerateAsync([content]);
		return new Vector(result[0].Vector);
	}

	private static readonly JsonSerializerOptions JsonOpts = new() {
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
	};

	private sealed class ExtractedMemory {
		public string? Content { get; set; }
		public string? ContactName { get; set; }
	}
}
