using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Persistence;

public class SessionFolderServiceTests :IDisposable {
	private readonly LisDbContext         db;
	private readonly FakeTimeProvider     time;
	private readonly SessionFolderService sut;

	public SessionFolderServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"folders-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new SessionFolderService(this.db, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>Ignores pgvector-specific model config that InMemory doesn't support.</summary>
	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	// ── CreateAsync ────────────────────────────────────────────────

	[Fact]
	public async Task CreateAsync_Persists_Folder_Under_Scope_With_Trimmed_Name() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "  Work  ");

		Assert.NotEqual(0,                 folder.Id);
		Assert.Equal("dir:/x",             folder.ScopeKey);
		Assert.Equal("Work",               folder.Name);
		Assert.Null(folder.ParentExternalId);
		Assert.Equal(0,                    folder.SortIndex);
		Assert.Equal(this.time.GetUtcNow(), folder.CreatedAt);
		Assert.Equal(this.time.GetUtcNow(), folder.UpdatedAt);
	}

	[Fact]
	public async Task CreateAsync_Empty_ScopeKey_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync("  ", "Work"));
	}

	[Fact]
	public async Task CreateAsync_Empty_Name_Throws() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync("dir:/x", "  "));
	}

	[Fact]
	public async Task CreateAsync_Assigns_Incrementing_SortIndex_Per_Scope_And_Parent() {
		SessionFolderEntity a = await this.sut.CreateAsync("dir:/x", "a");
		SessionFolderEntity b = await this.sut.CreateAsync("dir:/x", "b");
		SessionFolderEntity c = await this.sut.CreateAsync("dir:/y", "c"); // different scope

		Assert.Equal(0, a.SortIndex);
		Assert.Equal(1, b.SortIndex);
		Assert.Equal(0, c.SortIndex); // separate sequence per scope
	}

	[Fact]
	public async Task CreateAsync_Nested_Folder_Persists_Parent_External_Id() {
		SessionFolderEntity parent = await this.sut.CreateAsync("dir:/x", "Parent");
		SessionFolderEntity child  = await this.sut.CreateAsync("dir:/x", "Child", parent.ExternalId);

		Assert.Equal(parent.ExternalId, child.ParentExternalId);
		Assert.Equal(0,                 child.SortIndex); // separate sequence under parent
	}

	[Fact]
	public async Task CreateAsync_Unknown_Parent_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.CreateAsync("dir:/x", "Child", "missing"));
	}

	[Fact]
	public async Task CreateAsync_Parent_In_Other_Scope_Throws_ArgumentException() {
		SessionFolderEntity parent = await this.sut.CreateAsync("dir:/a", "P");
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync("dir:/b", "C", parent.ExternalId));
	}

	// ── ListForScopeAsync ──────────────────────────────────────────

	[Fact]
	public async Task ListForScopeAsync_Filters_By_Scope_And_Orders_By_Sort() {
		await this.sut.CreateAsync("dir:/x", "a");
		await this.sut.CreateAsync("dir:/x", "b");
		await this.sut.CreateAsync("dir:/y", "z");

		IReadOnlyList<SessionFolderEntity> result = await this.sut.ListForScopeAsync("dir:/x");

		Assert.Equal(2, result.Count);
		Assert.Equal(["a", "b"], result.Select(f => f.Name));
	}

	// ── RenameAsync ────────────────────────────────────────────────

	[Fact]
	public async Task RenameAsync_Updates_Name_And_UpdatedAt() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "Old");
		this.time.Advance(TimeSpan.FromMinutes(1));

		SessionFolderEntity renamed = await this.sut.RenameAsync(folder.ExternalId, "  New  ");

		Assert.Equal("New", renamed.Name);
		Assert.Equal(this.time.GetUtcNow(), renamed.UpdatedAt);
	}

	[Fact]
	public async Task RenameAsync_Empty_Name_Throws() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "Folder");
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.RenameAsync(folder.ExternalId, "  "));
	}

	[Fact]
	public async Task RenameAsync_Missing_Throws_KeyNotFound() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.RenameAsync("missing", "x"));
	}

	// ── DeleteAsync ────────────────────────────────────────────────

	[Fact]
	public async Task DeleteAsync_Removes_Folder_And_Orphans_Children() {
		SessionFolderEntity parent = await this.sut.CreateAsync("dir:/x", "P");
		SessionFolderEntity child  = await this.sut.CreateAsync("dir:/x", "C", parent.ExternalId);

		await this.sut.DeleteAsync(parent.ExternalId);

		Assert.Null(await this.db.SessionFolders.FirstOrDefaultAsync(f => f.ExternalId == parent.ExternalId));
		SessionFolderEntity? reloadedChild = await this.db.SessionFolders.FirstOrDefaultAsync(f => f.ExternalId == child.ExternalId);
		Assert.NotNull(reloadedChild);
		Assert.Null(reloadedChild.ParentExternalId);
	}

	[Fact]
	public async Task DeleteAsync_Cascades_Items() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		await this.sut.AddSessionsAsync(folder.ExternalId, ["chat-1", "chat-2"]);

		await this.sut.DeleteAsync(folder.ExternalId);

		Assert.Empty(await this.db.SessionFolderItems.Where(i => i.FolderId == folder.Id).ToListAsync());
	}

	// ── ReorderAsync ───────────────────────────────────────────────

	[Fact]
	public async Task ReorderAsync_Updates_Sort_Index_By_Position() {
		SessionFolderEntity a = await this.sut.CreateAsync("dir:/x", "a");
		SessionFolderEntity b = await this.sut.CreateAsync("dir:/x", "b");
		SessionFolderEntity c = await this.sut.CreateAsync("dir:/x", "c");

		this.time.Advance(TimeSpan.FromMinutes(1));
		await this.sut.ReorderAsync("dir:/x", [c.ExternalId, a.ExternalId, b.ExternalId]);

		List<SessionFolderEntity> after = await this.db.SessionFolders
			.Where(f => f.ScopeKey == "dir:/x")
			.OrderBy(f => f.SortIndex).ToListAsync();
		Assert.Equal([c.ExternalId, a.ExternalId, b.ExternalId], after.Select(f => f.ExternalId));
	}

	[Fact]
	public async Task ReorderAsync_Empty_Is_NoOp() {
		await this.sut.ReorderAsync("dir:/x", []);
		// Should not throw.
	}

	// ── AddSessionsAsync ───────────────────────────────────────────

	[Fact]
	public async Task AddSessionsAsync_Adds_Chats_With_Incrementing_SortIndex() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		IReadOnlyList<SessionFolderItemEntity> items = await this.sut.AddSessionsAsync(folder.ExternalId, ["a", "b", "c"]);

		Assert.Equal(3, items.Count);
		Assert.Equal(["a", "b", "c"], items.OrderBy(i => i.SortIndex).Select(i => i.ChatExternalId));
		Assert.Equal([0, 1, 2],       items.OrderBy(i => i.SortIndex).Select(i => i.SortIndex));
	}

	[Fact]
	public async Task AddSessionsAsync_Skips_Duplicates() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		await this.sut.AddSessionsAsync(folder.ExternalId, ["a", "b"]);
		IReadOnlyList<SessionFolderItemEntity> items = await this.sut.AddSessionsAsync(folder.ExternalId, ["b", "c"]);

		Assert.Equal(3, items.Count); // a, b, c — not a, b, b, c
		Assert.Equal(["a", "b", "c"], items.OrderBy(i => i.SortIndex).Select(i => i.ChatExternalId));
	}

	[Fact]
	public async Task AddSessionsAsync_Missing_Folder_Throws() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.AddSessionsAsync("missing", ["a"]));
	}

	// ── RemoveSessionAsync ─────────────────────────────────────────

	[Fact]
	public async Task RemoveSessionAsync_Removes_Item() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		await this.sut.AddSessionsAsync(folder.ExternalId, ["a", "b"]);

		await this.sut.RemoveSessionAsync(folder.ExternalId, "a");

		List<SessionFolderItemEntity> after = await this.db.SessionFolderItems.Where(i => i.FolderId == folder.Id).ToListAsync();
		Assert.Single(after);
		Assert.Equal("b", after[0].ChatExternalId);
	}

	[Fact]
	public async Task RemoveSessionAsync_Missing_Item_Is_NoOp() {
		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		// Should not throw.
		await this.sut.RemoveSessionAsync(folder.ExternalId, "absent");
	}

	// ── CleanupAsync ───────────────────────────────────────────────

	[Fact]
	public async Task CleanupAsync_Removes_Items_For_Missing_Chats() {
		// Seed a chat that exists
		this.db.Chats.Add(new ChatEntity {
			ExternalId = "live",
			Channel    = "test",
			CreatedAt  = this.time.GetUtcNow(),
			UpdatedAt  = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();

		SessionFolderEntity folder = await this.sut.CreateAsync("dir:/x", "F");
		await this.sut.AddSessionsAsync(folder.ExternalId, ["live", "ghost-1", "ghost-2"]);

		int removed = await this.sut.CleanupAsync();

		Assert.Equal(2, removed);
		List<SessionFolderItemEntity> after = await this.db.SessionFolderItems.Where(i => i.FolderId == folder.Id).ToListAsync();
		Assert.Single(after);
		Assert.Equal("live", after[0].ChatExternalId);
	}
}
