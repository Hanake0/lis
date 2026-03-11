using Lis.Agent;
using Lis.Core.Channel;
using Lis.Persistence.Entities;

using Microsoft.Extensions.Logging.Abstractions;

namespace Lis.Tests.Agent;

public class AgentServiceTests {
	private readonly AgentService _sut = new(NullLogger<AgentService>.Instance);

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
}
