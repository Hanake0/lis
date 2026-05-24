using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Persistence;

public class ProjectServiceTests :IDisposable {
	private readonly LisDbContext db;
	private readonly FakeTimeProvider time;
	private readonly ProjectService sut;

	public ProjectServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"projects-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new ProjectService(this.db, this.time);
	}

	/// <summary>Ignores pgvector-specific model config that InMemory doesn't support.</summary>
	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	// ── CreateAsync ────────────────────────────────────────────────

	[Fact]
	public async Task CreateAsync_Persists_Project_With_DerivedLabel_And_AutoColor() {
		ProjectEntity project = await this.sut.CreateAsync("/home/user/my-app");

		Assert.NotEqual(0, project.Id);
		Assert.NotEmpty(project.ExternalId);
		Assert.Equal("/home/user/my-app", project.Path);
		Assert.Equal("My App", project.Label);
		Assert.NotNull(project.Color);
		Assert.Equal(0, project.SortIndex);
		Assert.Equal(this.time.GetUtcNow(), project.AddedAt);
		Assert.Equal(this.time.GetUtcNow(), project.CreatedAt);
		Assert.Equal(this.time.GetUtcNow(), project.UpdatedAt);
	}

	[Fact]
	public async Task CreateAsync_Normalizes_Backslashes_And_Trailing_Slash() {
		ProjectEntity project = await this.sut.CreateAsync("C:\\bkp\\Dev\\app/");
		Assert.Equal("C:/bkp/Dev/app", project.Path);
	}

	[Fact]
	public async Task CreateAsync_Is_Idempotent_For_Same_Path() {
		ProjectEntity first  = await this.sut.CreateAsync("/repo");
		ProjectEntity second = await this.sut.CreateAsync("/repo");
		Assert.Equal(first.Id, second.Id);
	}

	[Fact]
	public async Task CreateAsync_Sets_Incrementing_SortIndex() {
		ProjectEntity a = await this.sut.CreateAsync("/a");
		ProjectEntity b = await this.sut.CreateAsync("/b");
		ProjectEntity c = await this.sut.CreateAsync("/c");
		Assert.Equal(0, a.SortIndex);
		Assert.Equal(1, b.SortIndex);
		Assert.Equal(2, c.SortIndex);
	}

	[Fact]
	public async Task CreateAsync_Empty_Path_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(""));
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync("   "));
	}

	[Fact]
	public async Task CreateAsync_Honours_Explicit_Label_And_Color() {
		ProjectEntity project = await this.sut.CreateAsync("/x", label: "Custom Label", color: "purple");
		Assert.Equal("Custom Label", project.Label);
		Assert.Equal("purple", project.Color);
	}

	[Fact]
	public async Task CreateAsync_DerivesLabel_Snake_And_Kebab_To_TitleCase() {
		ProjectEntity a = await this.sut.CreateAsync("/some_snake_case_app");
		ProjectEntity b = await this.sut.CreateAsync("/another-kebab-app");
		Assert.Equal("Some Snake Case App", a.Label);
		Assert.Equal("Another Kebab App", b.Label);
	}

	// ── ListAsync ──────────────────────────────────────────────────

	[Fact]
	public async Task ListAsync_Returns_Projects_Sorted_By_SortIndex() {
		await this.sut.CreateAsync("/a");
		ProjectEntity b = await this.sut.CreateAsync("/b");
		await this.sut.CreateAsync("/c");

		b.SortIndex = 99; // push b to the end manually
		await this.db.SaveChangesAsync();

		IReadOnlyList<ProjectEntity> all = await this.sut.ListAsync();
		Assert.Equal(["/a", "/c", "/b"], all.Select(p => p.Path));
	}

	[Fact]
	public async Task ListAsync_Empty_Returns_Empty_List() {
		Assert.Empty(await this.sut.ListAsync());
	}

	// ── UpdateMetaAsync ────────────────────────────────────────────

	[Fact]
	public async Task UpdateMetaAsync_Patches_Only_Provided_Fields() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		string originalColor = project.Color!;

		this.time.Advance(TimeSpan.FromMinutes(5));
		ProjectEntity updated = await this.sut.UpdateMetaAsync(
			project.ExternalId, new ProjectMetaUpdate(Label: "Renamed", SidebarCollapsed: true));

		Assert.Equal("Renamed", updated.Label);
		Assert.True(updated.SidebarCollapsed);
		Assert.Equal(originalColor, updated.Color); // untouched
		Assert.Equal(this.time.GetUtcNow(), updated.UpdatedAt);
	}

	[Fact]
	public async Task UpdateMetaAsync_Unknown_ExternalId_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.UpdateMetaAsync("nope", new ProjectMetaUpdate(Label: "X")));
	}

	// ── Icon image ─────────────────────────────────────────────────

	[Fact]
	public async Task SetIconImageAsync_Stores_Blob_With_Metadata() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		byte[] png = [137, 80, 78, 71]; // PNG signature prefix; content doesn't matter here.

		ProjectEntity updated = await this.sut.SetIconImageAsync(project.ExternalId, "image/png", png, "custom");

		Assert.Equal("image/png", updated.IconImageMime);
		Assert.Equal(png, updated.IconImageBlob);
		Assert.Equal("custom", updated.IconImageSource);
		Assert.NotNull(updated.IconImageUpdatedAt);
	}

	[Fact]
	public async Task SetIconImageAsync_Rejects_Bad_Inputs() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.SetIconImageAsync(project.ExternalId, "", [1], "custom"));
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.SetIconImageAsync(project.ExternalId, "image/png", [], "custom"));
		await Assert.ThrowsAsync<ArgumentException>(() =>
			this.sut.SetIconImageAsync(project.ExternalId, "image/png", [1], "bogus"));
	}

	[Fact]
	public async Task RemoveIconAsync_Clears_All_Icon_Fields() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		await this.sut.SetIconImageAsync(project.ExternalId, "image/png", [1, 2, 3], "auto");

		ProjectEntity cleared = await this.sut.RemoveIconAsync(project.ExternalId);
		Assert.Null(cleared.IconImageMime);
		Assert.Null(cleared.IconImageBlob);
		Assert.Null(cleared.IconImageSource);
		Assert.Null(cleared.IconImageUpdatedAt);
	}

	// ── DeleteAsync + chat detach ──────────────────────────────────

	[Fact]
	public async Task DeleteAsync_Removes_Project() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		await this.sut.DeleteAsync(project.ExternalId);

		Assert.Null(await this.sut.GetByExternalIdAsync(project.ExternalId));
	}

	[Fact]
	public async Task DeleteAsync_Unknown_Throws() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.DeleteAsync("nope"));
	}

	// ── ReorderAsync ───────────────────────────────────────────────

	[Fact]
	public async Task ReorderAsync_Applies_Given_Order() {
		ProjectEntity a = await this.sut.CreateAsync("/a");
		ProjectEntity b = await this.sut.CreateAsync("/b");
		ProjectEntity c = await this.sut.CreateAsync("/c");

		await this.sut.ReorderAsync([c.ExternalId, a.ExternalId, b.ExternalId]);

		List<ProjectEntity> after = (await this.sut.ListAsync()).ToList();
		Assert.Equal(["/c", "/a", "/b"], after.Select(p => p.Path));
		Assert.Equal(0, after[0].SortIndex);
		Assert.Equal(1, after[1].SortIndex);
		Assert.Equal(2, after[2].SortIndex);
	}

	[Fact]
	public async Task ReorderAsync_Ignores_Unknown_Ids() {
		ProjectEntity a = await this.sut.CreateAsync("/a");

		// Should not throw; unknown id is silently skipped (idempotent reorder).
		await this.sut.ReorderAsync(["unknown", a.ExternalId]);
		ProjectEntity reloaded = (await this.sut.ListAsync())[0];
		Assert.Equal(1, reloaded.SortIndex); // skipped 0 because "unknown" was first
	}

	[Fact]
	public async Task ReorderAsync_Empty_Is_NoOp() {
		ProjectEntity a = await this.sut.CreateAsync("/a");
		await this.sut.ReorderAsync([]);
		Assert.Equal(0, (await this.sut.ListAsync())[0].SortIndex);
	}

	// ── TouchLastOpenedAsync ───────────────────────────────────────

	[Fact]
	public async Task TouchLastOpenedAsync_Sets_Timestamp() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		Assert.Null(project.LastOpenedAt);

		this.time.Advance(TimeSpan.FromMinutes(10));
		ProjectEntity touched = await this.sut.TouchLastOpenedAsync(project.ExternalId);
		Assert.Equal(this.time.GetUtcNow(), touched.LastOpenedAt);
	}

	// ── MoveChatToProjectAsync ─────────────────────────────────────

	[Fact]
	public async Task MoveChatToProjectAsync_Attaches_Chat_To_Project() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		ChatEntity chat = new() { ExternalId = "chat:1", CreatedAt = this.time.GetUtcNow(), UpdatedAt = this.time.GetUtcNow() };
		this.db.Chats.Add(chat);
		await this.db.SaveChangesAsync();

		ChatEntity moved = await this.sut.MoveChatToProjectAsync("chat:1", project.ExternalId);
		Assert.Equal(project.Id, moved.ProjectId);
	}

	[Fact]
	public async Task MoveChatToProjectAsync_Null_Detaches_Chat() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		ChatEntity chat = new() { ExternalId = "chat:1", ProjectId = project.Id, CreatedAt = this.time.GetUtcNow(), UpdatedAt = this.time.GetUtcNow() };
		this.db.Chats.Add(chat);
		await this.db.SaveChangesAsync();

		ChatEntity detached = await this.sut.MoveChatToProjectAsync("chat:1", null);
		Assert.Null(detached.ProjectId);
	}

	[Fact]
	public async Task MoveChatToProjectAsync_Unknown_Chat_Throws() {
		ProjectEntity project = await this.sut.CreateAsync("/p");
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.MoveChatToProjectAsync("nope", project.ExternalId));
	}

	[Fact]
	public async Task MoveChatToProjectAsync_Unknown_Project_Throws() {
		ChatEntity chat = new() { ExternalId = "chat:1", CreatedAt = this.time.GetUtcNow(), UpdatedAt = this.time.GetUtcNow() };
		this.db.Chats.Add(chat);
		await this.db.SaveChangesAsync();

		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.MoveChatToProjectAsync("chat:1", "nope"));
	}
}
