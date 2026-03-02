using Lis.Agent;
using Lis.Persistence.Entities;

using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Conversation;

public sealed class ContextWindowBuilderTests {
	private readonly ContextWindowBuilder builder = new();

	[Fact]
	public void Build_IncludesSystemPrompt() {
		string systemPrompt = "You are Lis.";
		List<MessageEntity> messages = [];

		ChatHistory history = this.builder.Build(systemPrompt, messages);

		Assert.Single(history);
		Assert.Equal(AuthorRole.System, history[0].Role);
		Assert.Equal(systemPrompt, history[0].Content);
	}

	[Fact]
	public void Build_IncludesRecentMessages() {
		string systemPrompt = "System";
		List<MessageEntity> messages = [
			CreateMessage("Hello", isFromMe: false, timestamp: 1),
			CreateMessage("Hi there!", isFromMe: true, timestamp: 2),
			CreateMessage("How are you?", isFromMe: false, timestamp: 3),
		];

		ChatHistory history = this.builder.Build(systemPrompt, messages);

		Assert.Equal(4, history.Count);
		Assert.Equal(AuthorRole.System, history[0].Role);
		Assert.Equal(AuthorRole.User, history[1].Role);
		Assert.Equal(AuthorRole.Assistant, history[2].Role);
		Assert.Equal(AuthorRole.User, history[3].Role);
	}

	[Fact]
	public void Build_InjectsParentSessionSummary() {
		string systemPrompt = "System";
		List<MessageEntity> messages = [
			CreateMessage("Hello", isFromMe: false, timestamp: 1),
		];
		SessionEntity parentSession = new() { Summary = "Previous context here." };

		ChatHistory history = this.builder.Build(systemPrompt, messages, parentSession: parentSession);

		Assert.Equal(3, history.Count);
		Assert.Equal(AuthorRole.System, history[0].Role);
		Assert.Equal(AuthorRole.Assistant, history[1].Role);
		Assert.Contains("Previous context here.", history[1].Content);
		Assert.Equal(AuthorRole.User, history[2].Role);
	}

	[Fact]
	public void Build_InjectsCurrentSessionSummary() {
		string systemPrompt = "System";
		List<MessageEntity> messages = [
			CreateMessage("Hello", isFromMe: false, timestamp: 1),
		];
		SessionEntity session = new() { Summary = "Earlier summary." };

		ChatHistory history = this.builder.Build(systemPrompt, messages, session: session);

		Assert.Equal(3, history.Count);
		Assert.Equal(AuthorRole.Assistant, history[1].Role);
		Assert.Contains("Earlier summary.", history[1].Content);
	}

	private static MessageEntity CreateMessage(string body, bool isFromMe, int timestamp) {
		return new MessageEntity {
			SenderId = isFromMe ? "me" : "user@example.com",
			IsFromMe = isFromMe,
			Body = body,
			Timestamp = DateTimeOffset.UtcNow.AddSeconds(timestamp),
		};
	}
}
