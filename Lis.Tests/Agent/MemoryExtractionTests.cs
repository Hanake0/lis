using Lis.Agent;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Lis.Tests.Agent;

public class MemoryExtractionTests : IDisposable {
	private readonly LisDbContext _db;
	private readonly Mock<IChatClient> _chatClientMock;

	public MemoryExtractionTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this._db = new TestDbContext(options);
		this._chatClientMock = new Mock<IChatClient>();
	}

	/// <summary>Ignores pgvector-specific model config that InMemory doesn't support.</summary>
	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) : LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	public void Dispose() {
		this._db.Dispose();
		GC.SuppressFinalize(this);
	}

	private MemoryExtractionService CreateService(IChatClient? chatClient = null) {
		ServiceCollection services = new();
		services.AddSingleton(this._db);
		services.AddScoped<LisDbContext>(sp => sp.GetRequiredService<LisDbContext>());
		ServiceProvider sp = services.BuildServiceProvider();

		Mock<IServiceScopeFactory> scopeFactoryMock = new();
		Mock<IServiceScope> scopeMock = new();
		Mock<IServiceProvider> spMock = new();
		spMock.Setup(x => x.GetService(typeof(LisDbContext))).Returns(this._db);
		spMock.Setup(x => x.GetService(typeof(IEmbeddingGenerator<string, Embedding<float>>))).Returns((object?)null);
		scopeMock.Setup(x => x.ServiceProvider).Returns(spMock.Object);
		scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

		return new MemoryExtractionService(
			chatClient ?? this._chatClientMock.Object,
			scopeFactoryMock.Object,
			NullLogger<MemoryExtractionService>.Instance
		);
	}

	private void SetupChatClientResponse(string responseText) {
		ChatResponse response = new(new ChatMessage(ChatRole.Assistant, responseText));
		this._chatClientMock
			.Setup(c => c.GetResponseAsync(
				It.IsAny<IList<ChatMessage>>(),
				It.IsAny<ChatOptions?>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(response);
	}

	// ── Extraction tests ────────────────────────────────────────────

	[Fact]
	public async Task ExtractAsync_ValidJsonArray_CreatesMemories() {
		this.SetupChatClientResponse("""[{"content": "Lucas likes coffee", "contact_name": "Lucas"}]""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: I really love coffee", "Assistant: Noted!"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Single(memories);
		Assert.Equal("Lucas likes coffee", memories[0].Content);
		Assert.Equal(1.0f, memories[0].RelevanceScore);
		Assert.NotNull(memories[0].ContactId);
	}

	[Fact]
	public async Task ExtractAsync_MultipleMemories_CreatesAll() {
		this.SetupChatClientResponse("""
			[
				{"content": "Lucas works at Acme"},
				{"content": "Meeting scheduled for Friday", "contact_name": "Bob"}
			]
			""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: I work at Acme, meeting with Bob on Friday"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Equal(2, memories.Count);
	}

	[Fact]
	public async Task ExtractAsync_EmptyArray_CreatesNoMemories() {
		this.SetupChatClientResponse("[]");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: hello", "Assistant: hi"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Empty(memories);
	}

	[Fact]
	public async Task ExtractAsync_MalformedJson_DoesNotCrash() {
		this.SetupChatClientResponse("this is not json at all");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: whatever"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Empty(memories);
	}

	[Fact]
	public async Task ExtractAsync_JsonInMarkdownFences_ParsesCorrectly() {
		this.SetupChatClientResponse("""
			```json
			[{"content": "Prefers dark mode"}]
			```
			""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: I prefer dark mode"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Single(memories);
		Assert.Equal("Prefers dark mode", memories[0].Content);
	}

	[Fact]
	public async Task ExtractAsync_NullContent_SkipsEntry() {
		this.SetupChatClientResponse("""[{"content": null}, {"content": "Valid memory"}]""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: test"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Single(memories);
		Assert.Equal("Valid memory", memories[0].Content);
	}

	[Fact]
	public async Task ExtractAsync_EmptyContent_SkipsEntry() {
		this.SetupChatClientResponse("""[{"content": ""}, {"content": "  "}, {"content": "Valid"}]""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: test"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Single(memories);
		Assert.Equal("Valid", memories[0].Content);
	}

	[Fact]
	public async Task ExtractAsync_LlmThrows_DoesNotCrash() {
		this._chatClientMock
			.Setup(c => c.GetResponseAsync(
				It.IsAny<IList<ChatMessage>>(),
				It.IsAny<ChatOptions?>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("API down"));

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: test"];

		// Should not throw
		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Empty(memories);
	}

	[Fact]
	public async Task ExtractAsync_ContactCreatedWhenMissing() {
		this.SetupChatClientResponse("""[{"content": "Ana likes tea", "contact_name": "Ana"}]""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: Ana told me she likes tea"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		ContactEntity? contact = await this._db.Contacts.FirstOrDefaultAsync(c => c.Name == "Ana");
		Assert.NotNull(contact);

		MemoryEntity memory = await this._db.Memories.Include(m => m.Contact).FirstAsync();
		Assert.Equal("Ana", memory.Contact!.Name);
	}

	[Fact]
	public async Task ExtractAsync_ExistingContactReused() {
		this._db.Contacts.Add(new ContactEntity {
			Name = "Bob",
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		this.SetupChatClientResponse("""[{"content": "Bob runs daily", "contact_name": "Bob"}]""");

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: Bob told me he runs every day"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		Assert.Equal(1, await this._db.Contacts.CountAsync());
		MemoryEntity memory = await this._db.Memories.FirstAsync();
		Assert.NotNull(memory.ContactId);
	}

	[Fact]
	public async Task ExtractAsync_MoreThan5_CapsAt5() {
		string json = """
			[
				{"content": "Fact 1"},
				{"content": "Fact 2"},
				{"content": "Fact 3"},
				{"content": "Fact 4"},
				{"content": "Fact 5"},
				{"content": "Fact 6"},
				{"content": "Fact 7"}
			]
			""";
		this.SetupChatClientResponse(json);

		MemoryExtractionService sut = this.CreateService();
		List<string> messages = ["User: lots of facts"];

		await sut.ExtractAsync(messages, CancellationToken.None);

		List<MemoryEntity> memories = await this._db.Memories.ToListAsync();
		Assert.Equal(5, memories.Count);
	}

	// ── Relevance decay tests ───────────────────────────────────────

	[Fact]
	public void CalculateRelevanceScore_JustAccessed_Returns1() {
		float score = MemoryExtractionService.CalculateRelevanceScore(DateTimeOffset.UtcNow);
		Assert.Equal(1.0f, score);
	}

	[Fact]
	public void CalculateRelevanceScore_15DaysAgo_ReturnsHalf() {
		DateTimeOffset accessed = DateTimeOffset.UtcNow.AddDays(-15);
		float score = MemoryExtractionService.CalculateRelevanceScore(accessed);
		Assert.Equal(0.75f, score, 0.01f);
	}

	[Fact]
	public void CalculateRelevanceScore_30DaysAgo_Returns05() {
		DateTimeOffset accessed = DateTimeOffset.UtcNow.AddDays(-30);
		float score = MemoryExtractionService.CalculateRelevanceScore(accessed);
		Assert.Equal(0.5f, score, 0.01f);
	}

	[Fact]
	public void CalculateRelevanceScore_60DaysAgo_ClampedToMin() {
		DateTimeOffset accessed = DateTimeOffset.UtcNow.AddDays(-60);
		float score = MemoryExtractionService.CalculateRelevanceScore(accessed);
		Assert.Equal(0.1f, score, 0.01f);
	}

	[Fact]
	public void CalculateRelevanceScore_NeverAccessed_Returns1() {
		float score = MemoryExtractionService.CalculateRelevanceScore(null);
		Assert.Equal(1.0f, score);
	}
}
