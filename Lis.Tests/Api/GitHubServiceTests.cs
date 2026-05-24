using Lis.Api.Services;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="GitHubService"/>. Live GitHub calls (SetTokenAsync's
/// validation hop, GetPrStatusAsync's PR query) are out of scope here — those
/// require a real token and network. We cover everything else: status with /
/// without identity, clear-token, token encryption round-trip, and pr-status
/// degraded path (no identity → empty results).
/// </summary>
public sealed class GitHubServiceTests :IDisposable {
	private readonly LisDbContext                    db;
	private readonly FakeTimeProvider                time;
	private readonly EphemeralDataProtectionProvider dataProtection;
	private readonly GitHubService                   sut;

	public GitHubServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"github-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db             = new TestDbContext(options);
		this.time           = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.dataProtection = new EphemeralDataProtectionProvider();
		this.sut            = new GitHubService(this.db, this.dataProtection, this.time);
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

	[Fact]
	public async Task GetStatusAsync_NoIdentity_Returns_Disconnected() {
		GitHubStatus status = await this.sut.GetStatusAsync();
		Assert.False(status.Connected);
		Assert.Null(status.Login);
	}

	[Fact]
	public async Task GetStatusAsync_With_Existing_Identity_Returns_Login_And_Scopes() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		this.db.GitHubIdentities.Add(new GitHubIdentityEntity {
			Login          = "octocat",
			TokenProtected = protector.Protect("ghp_xxx"),
			Scopes         = "repo,read:user",
			AddedAt        = this.time.GetUtcNow(),
			UpdatedAt      = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();

		GitHubStatus status = await this.sut.GetStatusAsync();

		Assert.True(status.Connected);
		Assert.Equal("octocat",         status.Login);
		Assert.Equal("repo,read:user",  status.Scopes);
	}

	[Fact]
	public async Task ClearTokenAsync_Removes_All_Identities() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		this.db.GitHubIdentities.Add(new GitHubIdentityEntity {
			Login          = "octocat",
			TokenProtected = protector.Protect("ghp_xxx"),
			AddedAt        = this.time.GetUtcNow(),
			UpdatedAt      = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();

		await this.sut.ClearTokenAsync();

		Assert.Empty(await this.db.GitHubIdentities.ToListAsync());
	}

	[Fact]
	public async Task ClearTokenAsync_NoIdentity_Is_NoOp() {
		await this.sut.ClearTokenAsync(); // should not throw
	}

	[Fact]
	public async Task SetTokenAsync_Empty_Throws_ArgumentException() {
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.SetTokenAsync(""));
	}

	[Fact]
	public async Task GetPrStatusAsync_Empty_Keys_Returns_Empty() {
		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusAsync([]);
		Assert.Empty(result);
	}

	[Fact]
	public async Task GetPrStatusAsync_No_Identity_Returns_Empty() {
		IReadOnlyDictionary<string, PrStatus> result = await this.sut.GetPrStatusAsync([
			new PrKey("octocat", "Hello-World", "main")
		]);
		Assert.Empty(result);
	}

	[Fact]
	public void Token_RoundTrips_Through_DataProtection() {
		IDataProtector protector = this.dataProtection.CreateProtector("Lis.GitHub.Token.v1");
		string protectedToken = protector.Protect("ghp_secret_abc");
		Assert.NotEqual("ghp_secret_abc", protectedToken);
		Assert.Equal("ghp_secret_abc",     protector.Unprotect(protectedToken));
	}
}
