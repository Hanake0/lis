using System.Text.Json;

using Lis.Agent;
using Lis.Core.Configuration;
using Lis.Persistence.Entities;

using Microsoft.SemanticKernel;
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

	[Fact]
	public void Build_PrunedToolResult_PreservesToolRoleAndCallId() {
		(MessageEntity assistantMsg, MessageEntity toolMsg) = CreateToolCallPair(
			id: 4, callId: "call-123", funcName: "get_weather", result: "Sunny, 25°C with gentle breeze");

		SessionEntity session = new() { ToolsPrunedThroughId = 10 };
		List<MessageEntity> messages = [assistantMsg, toolMsg];

		ChatHistory history = this.builder.Build("System", messages, session: session);

		// system + assistant (with FunctionCallContent) + pruned tool
		Assert.Equal(3, history.Count);
		ChatMessageContent pruned = history[2];
		Assert.Equal(AuthorRole.Tool, pruned.Role);

		FunctionResultContent? frc = pruned.Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc);
		Assert.Equal("call-123", frc.CallId);
		Assert.Equal("get_weather", frc.FunctionName);
		Assert.Equal("get_weather", frc.Result?.ToString());
	}

	[Fact]
	public void Build_KeepAllPolicy_SkipsPruning() {
		(MessageEntity assistantMsg, MessageEntity toolMsg) = CreateToolCallPair(
			id: 4, callId: "call-1", funcName: "my_tool", result: "Full output text");

		SessionEntity session = new() { ToolsPrunedThroughId = 10 };
		LisOptions options = new() { ToolSummarizationPolicy = "keep_all" };
		List<MessageEntity> messages = [assistantMsg, toolMsg];

		ChatHistory history = this.builder.Build("System", messages, session: session, options: options);

		// Tool message should be kept in full (not pruned)
		ChatMessageContent result = history[2];
		Assert.Equal(AuthorRole.Tool, result.Role);
		FunctionResultContent? frc = result.Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc);
		Assert.Equal("Full output text", frc.Result?.ToString());
	}

	[Fact]
	public void Build_KeepNonePolicy_PrunesBeforeBoundary() {
		(MessageEntity assistantMsg, MessageEntity toolMsg) = CreateToolCallPair(
			id: 4, callId: "call-1", funcName: "my_tool", result: "Full output text");

		SessionEntity session = new() { ToolsPrunedThroughId = 10 };
		LisOptions options = new() { ToolSummarizationPolicy = "keep_none" };
		List<MessageEntity> messages = [assistantMsg, toolMsg];

		ChatHistory history = this.builder.Build("System", messages, session: session, options: options);

		ChatMessageContent result = history[2];
		Assert.Equal(AuthorRole.Tool, result.Role);
		FunctionResultContent? frc = result.Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc);
		Assert.Equal("my_tool", frc.Result?.ToString());
	}

	[Fact]
	public void Build_KeepNonePolicy_DoesNotPruneAfterBoundary() {
		(MessageEntity assistantMsg, MessageEntity toolMsg) = CreateToolCallPair(
			id: 9, callId: "call-1", funcName: "my_tool", result: "Full output text");

		SessionEntity session = new() { ToolsPrunedThroughId = 5 };
		LisOptions options = new() { ToolSummarizationPolicy = "keep_none" };
		List<MessageEntity> messages = [assistantMsg, toolMsg]; // ids 9 and 10, both after boundary

		ChatHistory history = this.builder.Build("System", messages, session: session, options: options);

		ChatMessageContent result = history[2];
		FunctionResultContent? frc = result.Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc);
		Assert.Equal("Full output text", frc.Result?.ToString());
	}

	[Fact]
	public void Build_ToolKeepThreshold_KeepsRecentToolOutputs() {
		(MessageEntity assist1, MessageEntity tool1) = CreateToolCallPair(
			id: 4, callId: "call-1", funcName: "old_tool", result: "Old output", outputTokens: 200);
		(MessageEntity assist2, MessageEntity tool2) = CreateToolCallPair(
			id: 14, callId: "call-2", funcName: "recent_tool", result: "Recent output", outputTokens: 50);

		SessionEntity session = new() { ToolsPrunedThroughId = 20 };
		LisOptions options = new() { ToolKeepThreshold = 100 };
		List<MessageEntity> messages = [assist1, tool1, assist2, tool2];

		ChatHistory history = this.builder.Build("System", messages, session: session, options: options);

		// system + assist1 + pruned tool1 + assist2 + kept tool2 = 5
		Assert.Equal(5, history.Count);

		// First tool (id=5) should be pruned
		FunctionResultContent? frc1 = history[2].Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc1);
		Assert.Equal("old_tool", frc1.Result?.ToString());

		// Second tool (id=15) should be kept in full
		FunctionResultContent? frc2 = history[4].Items.OfType<FunctionResultContent>().FirstOrDefault();
		Assert.NotNull(frc2);
		Assert.Equal("Recent output", frc2.Result?.ToString());
	}

	private static MessageEntity CreateMessage(string body, bool isFromMe, int timestamp) {
		return new MessageEntity {
			SenderId = isFromMe ? "me" : "user@example.com",
			IsFromMe = isFromMe,
			Body = body,
			Timestamp = DateTimeOffset.UtcNow.AddSeconds(timestamp),
		};
	}

	/// <summary>
	/// Creates a matched assistant+tool pair: assistant has FunctionCallContent,
	/// tool has FunctionResultContent with matching CallId. IDs are id and id+1.
	/// </summary>
	private static (MessageEntity Assistant, MessageEntity Tool) CreateToolCallPair(
		long id, string callId, string funcName, string result, int outputTokens = 0) {

		// Assistant message with FunctionCallContent
		ChatMessageContent assistantMsg = new(AuthorRole.Assistant, content: (string?)null);
		assistantMsg.Items.Add(new FunctionCallContent(callId, funcName));

		// Tool message with FunctionResultContent (constructor: functionName, pluginName, callId, result)
		ChatMessageContent toolMsg = new(AuthorRole.Tool, content: (string?)null);
		toolMsg.Items.Add(new FunctionResultContent(funcName, null, callId, result));

		MessageEntity assistant = new() {
			Id = id,
			SenderId = "me",
			IsFromMe = true,
			Role = "assistant",
			SkContent = JsonSerializer.Serialize(assistantMsg),
			Timestamp = DateTimeOffset.UtcNow.AddSeconds(id),
		};

		MessageEntity tool = new() {
			Id = id + 1,
			SenderId = "me",
			IsFromMe = true,
			Role = "tool",
			SkContent = JsonSerializer.Serialize(toolMsg),
			OutputTokens = outputTokens > 0 ? outputTokens : null,
			Timestamp = DateTimeOffset.UtcNow.AddSeconds(id + 1),
		};

		return (assistant, tool);
	}
}
