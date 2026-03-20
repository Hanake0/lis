using Lis.Agent;
using Lis.Core.Channel;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lis.Tests.Agent;

public class AgentServiceTests : IDisposable {
	private readonly AgentService _sut = new(NullLogger<AgentService>.Instance);
	private readonly LisDbContext _db;

	public AgentServiceTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this._db = new TestDbContext(options);
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

	private async Task<AgentEntity> SeedDefaultAgentAsync(string displayName = "Lis") {
		AgentEntity agent = new() {
			Name = "default", DisplayName = displayName, Model = "test-model", IsDefault = true,
			CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
		};
		this._db.Agents.Add(agent);
		await this._db.SaveChangesAsync();
		return agent;
	}

	[Fact]
	public void ShouldRespond_DisabledChat_ReturnsFalse() {
		ChatEntity chat = new() { ExternalId = "c1", Enabled = false };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "owner" };

		bool result = this._sut.ShouldRespond(chat, msg, "owner");

		Assert.False(result);
	}

	[Fact]
	public void ShouldRespond_Owner_ReturnsTrue() {
		ChatEntity chat = new() { ExternalId = "c1", Enabled = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "owner@jid" };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.True(result);
	}

	[Fact]
	public void ShouldRespond_AllowedSender_ReturnsTrue() {
		ChatEntity chat = new() {
			ExternalId = "c1",
			Enabled    = true,
			AllowedSenders = [
				new ChatAllowedSenderEntity { SenderId = "allowed@jid" }
			]
		};
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "allowed@jid" };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.True(result);
	}

	[Fact]
	public void ShouldRespond_UnknownSender_ReturnsFalse() {
		ChatEntity chat = new() { ExternalId = "c1", Enabled = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "stranger@jid" };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.False(result);
	}

	[Fact]
	public void ShouldRespond_GroupWithRequireMention_DeniesUnmentioned() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true, RequireMention = true, OpenGroup = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "stranger", IsGroup = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.False(result);
	}

	[Fact]
	public void ShouldRespond_GroupWithRequireMention_AllowsMentioned() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true, RequireMention = true, OpenGroup = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "stranger", IsGroup = true, IsBotMentioned = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.True(result);
	}

	[Fact]
	public void ShouldRespond_GroupOwnerBypassesMention() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true, RequireMention = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "owner@jid", IsGroup = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.True(result);
	}

	[Fact]
	public void ShouldRespond_OpenGroup_AllowsAnySender() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true, OpenGroup = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "stranger", IsGroup = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.True(result);
	}

	[Fact]
	public void ShouldRespond_OpenGroupRequireMention_DeniesWithoutMention() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true, OpenGroup = true, RequireMention = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "stranger", IsGroup = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.False(result);
	}

	[Fact]
	public void ShouldRespond_ClosedGroupStranger_Denied() {
		ChatEntity chat = new() { ExternalId = "g1", Enabled = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "stranger", IsGroup = true };

		bool result = this._sut.ShouldRespond(chat, msg, "owner@jid");

		Assert.False(result);
	}

	[Fact]
	public void ShouldRespond_EmptyOwnerJid_DeniesAll() {
		ChatEntity chat = new() { ExternalId = "c1", Enabled = true };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "anyone" };

		bool result = this._sut.ShouldRespond(chat, msg, "");

		Assert.False(result);
	}

	[Fact]
	public void ToModelSettings_MapsAllFields() {
		AgentEntity agent = new() {
			Name           = "test",
			Model          = "claude-opus-4-6",
			MaxTokens      = 8192,
			ContextBudget  = 50000,
			ThinkingEffort = "high"
		};

		var settings = AgentService.ToModelSettings(agent);

		Assert.Equal("claude-opus-4-6", settings.Model);
		Assert.Equal(8192, settings.MaxTokens);
		Assert.Equal(50000, settings.ContextBudget);
		Assert.Equal("high", settings.ThinkingEffort);
	}

	// ── DetectMentionAsync ──────────────────────────────────────────────

	[Fact]
	public async Task DetectMention_NonGroupMessage_Skipped() {
		ChatEntity chat = new() { ExternalId = "c1" };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "c1", SenderId = "s1", IsGroup = false, Body = "Lis hello" };

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.False(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_AlreadyMentioned_Skipped() {
		ChatEntity chat = new() { ExternalId = "g1" };
		IncomingMessage msg = new() { ExternalId = "m1", ChatId = "g1", SenderId = "s1", IsGroup = true, IsBotMentioned = true };

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.True(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_ReplyToBot_SetsMentioned() {
		AgentEntity agent = await this.SeedDefaultAgentAsync();
		ChatEntity chat = new() { ExternalId = "g1", AgentId = agent.Id, Agent = agent };
		this._db.Messages.Add(new MessageEntity {
			ExternalId = "bot-msg-1", ChatId = 1, SessionId = 1, SenderId = "me", IsFromMe = true,
			Timestamp = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		IncomingMessage msg = new() {
			ExternalId = "m1", ChatId = "g1", SenderId = "s1",
			IsGroup = true, RepliedId = "bot-msg-1"
		};

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.True(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_ReplyToNonBot_StaysFalse() {
		AgentEntity agent = await this.SeedDefaultAgentAsync();
		ChatEntity chat = new() { ExternalId = "g1", AgentId = agent.Id, Agent = agent };
		this._db.Messages.Add(new MessageEntity {
			ExternalId = "user-msg-1", ChatId = 1, SessionId = 1, SenderId = "user@jid", IsFromMe = false,
			Timestamp = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
		});
		await this._db.SaveChangesAsync();

		IncomingMessage msg = new() {
			ExternalId = "m1", ChatId = "g1", SenderId = "s1",
			IsGroup = true, RepliedId = "user-msg-1"
		};

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.False(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_TextContainsBotName_SetsMentioned() {
		AgentEntity agent = await this.SeedDefaultAgentAsync("Lis");
		ChatEntity chat = new() { ExternalId = "g1", AgentId = agent.Id, Agent = agent };

		IncomingMessage msg = new() {
			ExternalId = "m1", ChatId = "g1", SenderId = "s1",
			IsGroup = true, Body = "hey lis what do you think?"
		};

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.True(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_TextDoesNotContainBotName_StaysFalse() {
		AgentEntity agent = await this.SeedDefaultAgentAsync("Lis");
		ChatEntity chat = new() { ExternalId = "g1", AgentId = agent.Id, Agent = agent };

		IncomingMessage msg = new() {
			ExternalId = "m1", ChatId = "g1", SenderId = "s1",
			IsGroup = true, Body = "hello everyone"
		};

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.False(msg.IsBotMentioned);
	}

	[Fact]
	public async Task DetectMention_NoBodyNoReply_StaysFalse() {
		AgentEntity agent = await this.SeedDefaultAgentAsync();
		ChatEntity chat = new() { ExternalId = "g1", AgentId = agent.Id, Agent = agent };

		IncomingMessage msg = new() {
			ExternalId = "m1", ChatId = "g1", SenderId = "s1", IsGroup = true
		};

		await this._sut.DetectMentionAsync(this._db, chat, msg, CancellationToken.None);

		Assert.False(msg.IsBotMentioned);
	}
}
