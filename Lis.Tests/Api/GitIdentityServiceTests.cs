using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="GitIdentityService"/>: CRUD round-trip, validation,
/// default-pointer lifecycle, cascade of the default pointer when the row
/// it points at is removed.
/// </summary>
public sealed class GitIdentityServiceTests :IDisposable {
	private readonly LisDbContext       db;
	private readonly FakeTimeProvider   time;
	private readonly GitIdentityService sut;

	public GitIdentityServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"git-id-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db   = new TestDbContext(options);
		this.time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.sut  = new GitIdentityService(this.db, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	private static GitIdentityEntity sample(string name = "Work", string email = "user@work.io") =>
		new() {
			ExternalId = "",
			Name       = name,
			UserName   = "User Name",
			UserEmail  = email,
			AuthType   = "ssh",
			SshKeyPath = "/home/u/.ssh/id_ed25519",
			Host       = "github.com",
			Color      = "purple",
			Icon       = "branch",
		};

	[Fact]
	public async Task ListAsync_Empty_Returns_Empty() {
		Assert.Empty(await this.sut.ListAsync());
	}

	[Fact]
	public async Task CreateAsync_Persists_With_Generated_ExternalId_When_Empty() {
		GitIdentityEntity created = await this.sut.CreateAsync(sample());
		Assert.NotEmpty(created.ExternalId);
		Assert.Equal("Work", created.Name);
		Assert.Equal("ssh", created.AuthType);
		Assert.Equal(this.time.GetUtcNow(), created.CreatedAt);
	}

	[Fact]
	public async Task CreateAsync_Honours_Caller_Supplied_ExternalId() {
		GitIdentityEntity input = sample();
		input.ExternalId = "my-fixed-id";
		GitIdentityEntity created = await this.sut.CreateAsync(input);
		Assert.Equal("my-fixed-id", created.ExternalId);
	}

	[Fact]
	public async Task CreateAsync_Validates_Required_Fields() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new GitIdentityEntity {
			ExternalId = "", Name = " ", UserName = "u", UserEmail = "u@x.io",
		}));
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new GitIdentityEntity {
			ExternalId = "", Name = "n", UserName = "", UserEmail = "u@x.io",
		}));
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(new GitIdentityEntity {
			ExternalId = "", Name = "n", UserName = "u", UserEmail = "not-an-email",
		}));
	}

	[Fact]
	public async Task CreateAsync_Rejects_Unknown_AuthType() {
		GitIdentityEntity input = sample();
		input.AuthType = "bearer";
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.CreateAsync(input));
	}

	[Fact]
	public async Task UpdateAsync_Mutates_Then_SaveChanges() {
		GitIdentityEntity created = await this.sut.CreateAsync(sample());
		this.time.Advance(TimeSpan.FromMinutes(5));

		GitIdentityEntity updated = await this.sut.UpdateAsync(created.ExternalId, row => {
			row.Name = "Personal";
			row.Color = "blue";
		});

		Assert.Equal("Personal", updated.Name);
		Assert.Equal("blue",     updated.Color);
		Assert.Equal(this.time.GetUtcNow(), updated.UpdatedAt);
	}

	[Fact]
	public async Task UpdateAsync_Throws_KeyNotFound_For_Missing_Id() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() =>
			this.sut.UpdateAsync("missing", _ => { }));
	}

	[Fact]
	public async Task DeleteAsync_Removes_The_Row() {
		GitIdentityEntity created = await this.sut.CreateAsync(sample());
		await this.sut.DeleteAsync(created.ExternalId);
		Assert.Null(await this.sut.GetByExternalIdAsync(created.ExternalId));
	}

	[Fact]
	public async Task DeleteAsync_Clears_The_Default_Pointer_When_It_Targeted_The_Removed_Row() {
		GitIdentityEntity created = await this.sut.CreateAsync(sample());
		await this.sut.SetDefaultIdentityExternalIdAsync(created.ExternalId);
		Assert.Equal(created.ExternalId, await this.sut.GetDefaultIdentityExternalIdAsync());

		await this.sut.DeleteAsync(created.ExternalId);

		Assert.Null(await this.sut.GetDefaultIdentityExternalIdAsync());
	}

	[Fact]
	public async Task SetDefaultIdentityExternalIdAsync_Stores_And_Replaces() {
		await this.sut.SetDefaultIdentityExternalIdAsync("a");
		Assert.Equal("a", await this.sut.GetDefaultIdentityExternalIdAsync());

		await this.sut.SetDefaultIdentityExternalIdAsync("b");
		Assert.Equal("b", await this.sut.GetDefaultIdentityExternalIdAsync());
	}

	[Fact]
	public async Task SetDefaultIdentityExternalIdAsync_Null_Clears_The_Pointer() {
		await this.sut.SetDefaultIdentityExternalIdAsync("a");
		await this.sut.SetDefaultIdentityExternalIdAsync(null);
		Assert.Null(await this.sut.GetDefaultIdentityExternalIdAsync());
	}

	[Fact]
	public async Task SetDefaultIdentityExternalIdAsync_Whitespace_Clears_The_Pointer() {
		await this.sut.SetDefaultIdentityExternalIdAsync("a");
		await this.sut.SetDefaultIdentityExternalIdAsync("   ");
		Assert.Null(await this.sut.GetDefaultIdentityExternalIdAsync());
	}

	[Fact]
	public async Task ListAsync_Returns_By_CreatedAt_Ascending() {
		await this.sut.CreateAsync(sample("First",  "first@x.io"));
		this.time.Advance(TimeSpan.FromSeconds(1));
		await this.sut.CreateAsync(sample("Second", "second@x.io"));
		this.time.Advance(TimeSpan.FromSeconds(1));
		await this.sut.CreateAsync(sample("Third",  "third@x.io"));

		IReadOnlyList<GitIdentityEntity> all = await this.sut.ListAsync();
		Assert.Equal(["First", "Second", "Third"], all.Select(x => x.Name));
	}
}
