using Lis.Agent;
using Lis.Core.Configuration;
using Lis.Persistence.Entities;

using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Conversation;

public sealed class ContextWindowBuilderTests {
	private readonly ContextWindowBuilder builder = new(new ModelSettings {
		MaxTokens     = 1000,
		ContextBudget = 4000,
	});

	[Fact]
	public void EstimateTokens_ReturnsExpectedValue() {
		string text = new('a', 350);

		int tokens = ContextWindowBuilder.EstimateTokens(text);

		Assert.Equal(100, tokens);
	}

	[Fact]
	public void EstimateTokens_NullText_ReturnsZero() {
		int tokens = ContextWindowBuilder.EstimateTokens(null);

		Assert.Equal(0, tokens);
	}

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
	public void Build_TruncatesWhenOverBudget() {
		string systemPrompt = "System";
		List<MessageEntity> messages = [];

		for (int i = 0; i < 100; i++) {
			messages.Add(CreateMessage(
				new string('x', 350),
				isFromMe: i % 2 == 0,
				timestamp: i));
		}

		ChatHistory history = this.builder.Build(systemPrompt, messages);

		Assert.True(history.Count < 101);
		Assert.True(history.Count > 1);
	}

	private static MessageEntity CreateMessage(string body, bool isFromMe, int timestamp) {
		return new MessageEntity {
			SenderId = isFromMe ? "me" : "user@example.com",
			IsFromMe = isFromMe,
			Body = body,
			TokenCount = ContextWindowBuilder.EstimateTokens(body),
			Timestamp = DateTimeOffset.UtcNow.AddSeconds(timestamp),
		};
	}
}
