using System.Text.Json;

using Lis.Core.Configuration;
using Lis.Persistence.Entities;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

public sealed class ContextWindowBuilder(ModelSettings modelSettings) {
	private const double CHARS_PER_TOKEN = 3.5;
	private const int TOOL_SCHEMA_RESERVE = 400;

	public static int EstimateTokens(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return 0;
		}

		return (int)Math.Ceiling(text.Length / CHARS_PER_TOKEN);
	}

	public ChatHistory Build(string systemPrompt, IReadOnlyList<MessageEntity> recentMessages) {
		int budget = modelSettings.ContextBudget;
		int responseReserve = modelSettings.MaxTokens;

		int systemTokens = EstimateTokens(systemPrompt);
		int available = budget - systemTokens - responseReserve - TOOL_SCHEMA_RESERVE;

		ChatHistory history = new(systemPrompt);

		int startIndex = 0;
		int accumulated = 0;
		for (int i = recentMessages.Count - 1; i >= 0; i--) {
			MessageEntity msg = recentMessages[i];
			int cost = msg.TokenCount > 0 ? msg.TokenCount : EstimateTokens(msg.Body);
			accumulated += cost;

			if (accumulated > available) {
				startIndex = i + 1;
				break;
			}
		}

		for (int i = startIndex; i < recentMessages.Count; i++) {
			MessageEntity msg = recentMessages[i];

			if (msg.SkContent is not null) {
				ChatMessageContent? skMsg = JsonSerializer.Deserialize<ChatMessageContent>(msg.SkContent);
				if (skMsg is not null) { history.Add(skMsg); continue; }
			}

			string content = msg.Body ?? "[media]";
			if (msg.IsFromMe) history.AddAssistantMessage(content);
			else              history.AddUserMessage(content);
		}

		return history;
	}
}
