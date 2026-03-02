using System.ComponentModel;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Lis.Tools;

public sealed class MemoryPlugin(IServiceScopeFactory scopeFactory) {

	[KernelFunction("create_memory")]
	[Description("Store a new memory. Optionally link to a person by name.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> CreateMemoryAsync(
		[Description("The information to remember")] string content,
		[Description("Person's name this memory is about (optional)")] string? contactName = null) {
		await ToolContext.NotifyAsync($"💾 Saving memory{(contactName is not null ? $"\ncontact: {contactName}" : "")}\n```\n{content}\n```");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long? contactId = await ResolveOrCreateContactAsync(db, contactName);
		Vector? embedding = await GenerateEmbeddingAsync(scope.ServiceProvider, content);

		MemoryEntity memory = new() {
			Content   = content.Trim(),
			ContactId = contactId,
			Embedding = embedding,
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};

		db.Memories.Add(memory);
		await db.SaveChangesAsync();

		return $"Memory #{memory.Id} saved.";
	}

	[KernelFunction("search_memories")]
	[Description("Search stored memories by keyword or phrase. Optionally filter by person.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	public async Task<string> SearchMemoriesAsync(
		[Description("Search keyword or phrase")] string query,
		[Description("Person's name to filter by (optional)")] string? contactName = null) {
		await ToolContext.NotifyAsync($"🔍 Searching memories\nquery: {query}{(contactName is not null ? $"\ncontact: {contactName}" : "")}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		long? contactId = null;
		if (!string.IsNullOrWhiteSpace(contactName)) {
			ContactEntity? contact = await db.Contacts
				.FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name!, contactName));
			contactId = contact?.Id;

			if (contactId is null) return $"No contact found named '{contactName}'.";
		}

		IEmbeddingGenerator<string, Embedding<float>>? embeddingGen =
			scope.ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();

		List<MemoryEntity> results;

		if (embeddingGen is not null) {
			results = await VectorSearchAsync(db, embeddingGen, query, contactId);
		} else {
			results = await FtsSearchAsync(db, query, contactId);
		}

		if (results.Count == 0) return "No memories found.";

		StringBuilder sb = new();
		foreach (MemoryEntity mem in results) {
			string prefix = mem.Contact is not null ? $"[{mem.Contact.Name}] " : "";
			sb.AppendLine($"#{mem.Id}: {prefix}{mem.Content}");
		}

		return sb.ToString().TrimEnd();
	}

	[KernelFunction("update_memory")]
	[Description("Update an existing memory's content.")]
	public async Task<string> UpdateMemoryAsync(
		[Description("Memory ID")] long id,
		[Description("Updated content")] string content) {
		await ToolContext.NotifyAsync($"✏️ Updating memory #{id}\n```\n{content}\n```");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		MemoryEntity? memory = await db.Memories.FindAsync(id);
		if (memory is null) return $"Memory #{id} not found.";

		memory.Content   = content.Trim();
		memory.Embedding = await GenerateEmbeddingAsync(scope.ServiceProvider, content);
		memory.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync();

		return $"Memory #{id} updated.";
	}

	[KernelFunction("delete_memory")]
	[Description("Delete a memory by ID.")]
	public async Task<string> DeleteMemoryAsync(
		[Description("Memory ID")] long id) {
		await ToolContext.NotifyAsync($"🗑️ Deleting memory #{id}");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		MemoryEntity? memory = await db.Memories.FindAsync(id);
		if (memory is null) return $"Memory #{id} not found.";

		db.Memories.Remove(memory);
		await db.SaveChangesAsync();

		return $"Memory #{id} deleted.";
	}

	private static async Task<long?> ResolveOrCreateContactAsync(LisDbContext db, string? contactName) {
		if (string.IsNullOrWhiteSpace(contactName)) return null;

		ContactEntity? contact = await db.Contacts
			.FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name!, contactName));

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

	private static async Task<Vector?> GenerateEmbeddingAsync(IServiceProvider sp, string content) {
		IEmbeddingGenerator<string, Embedding<float>>? embeddingGen =
			sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>();

		if (embeddingGen is null) return null;

		GeneratedEmbeddings<Embedding<float>> result = await embeddingGen.GenerateAsync([content]);
		return new Vector(result[0].Vector);
	}

	private static async Task<List<MemoryEntity>> VectorSearchAsync(
		LisDbContext db,
		IEmbeddingGenerator<string, Embedding<float>> embeddingGen,
		string query,
		long? contactId) {

		GeneratedEmbeddings<Embedding<float>> result = await embeddingGen.GenerateAsync([query]);
		Vector queryVector = new(result[0].Vector);

		IQueryable<MemoryEntity> q = db.Memories
			.Include(m => m.Contact)
			.Where(m => m.Embedding != null);

		if (contactId is not null)
			q = q.Where(m => m.ContactId == contactId);

		return await q
			.OrderBy(m => m.Embedding!.CosineDistance(queryVector))
			.Take(10)
			.ToListAsync();
	}

	private static async Task<List<MemoryEntity>> FtsSearchAsync(
		LisDbContext db,
		string query,
		long? contactId) {

		IQueryable<MemoryEntity> q = db.Memories.Include(m => m.Contact);

		if (contactId is not null)
			q = q.Where(m => m.ContactId == contactId);

		return await q
			.Where(m => EF.Functions.ToTsVector("simple", m.Content)
				.Matches(EF.Functions.WebSearchToTsQuery("simple", query)))
			.OrderByDescending(m => EF.Functions.ToTsVector("simple", m.Content)
				.Rank(EF.Functions.WebSearchToTsQuery("simple", query)))
			.Take(10)
			.ToListAsync();
	}
}
