using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Agent;

/// <summary>
/// Converts a Semantic Kernel ChatHistory into Anthropic's /v1/messages JSON format.
/// Used by ITokenCounter to count tokens via the count_tokens endpoint.
/// </summary>
public static class ChatHistorySerializer {
	public static string ToAnthropicJson(ChatHistory history, string model) {
		JsonArray system = [];
		JsonArray messages = [];

		foreach (ChatMessageContent msg in history) {
			if (msg.Role == AuthorRole.System) {
				system.Add(new JsonObject { ["type"] = "text", ["text"] = msg.Content ?? "" });
				continue;
			}

			if (msg.Role == AuthorRole.Tool) {
				// Anthropic: tool results are sent as role=user with tool_result content blocks
				JsonArray content = [];
				foreach (FunctionResultContent frc in msg.Items.OfType<FunctionResultContent>())
					content.Add(new JsonObject {
						["type"]        = "tool_result",
						["tool_use_id"] = frc.CallId ?? "",
						["content"]     = frc.Result?.ToString() ?? ""
					});

				if (content.Count > 0)
					messages.Add(new JsonObject { ["role"] = "user", ["content"] = content });
				else
					messages.Add(new JsonObject { ["role"] = "user", ["content"] = msg.Content ?? "" });
				continue;
			}

			if (msg.Role == AuthorRole.Assistant) {
				List<FunctionCallContent> calls = msg.Items.OfType<FunctionCallContent>().ToList();

				if (calls.Count == 0) {
					// Text-only assistant message
					messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = msg.Content ?? "" });
				} else {
					// Assistant message with tool calls (may also have text)
					JsonArray content = [];
					if (msg.Content is { Length: > 0 } text)
						content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
					foreach (FunctionCallContent call in calls) {
						JsonNode? input = call.Arguments is not null
							? JsonNode.Parse(JsonSerializer.Serialize(call.Arguments))
							: new JsonObject();
						content.Add(new JsonObject {
							["type"]  = "tool_use",
							["id"]    = call.Id ?? "",
							["name"]  = call.FunctionName ?? "",
							["input"] = input ?? new JsonObject()
						});
					}
					messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
				}
				continue;
			}

			// User messages
			messages.Add(new JsonObject { ["role"] = "user", ["content"] = msg.Content ?? "" });
		}

		// Merge consecutive same-role messages (Anthropic requires alternating roles)
		JsonArray merged = MergeConsecutiveRoles(messages);

		JsonObject root = new() {
			["model"]   = model,
			["system"]  = system,
			["messages"] = merged
		};

		return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Anthropic requires strictly alternating user/assistant roles.
	/// Merges consecutive messages with the same role into a single message
	/// with a content array containing all blocks.
	/// </summary>
	private static JsonArray MergeConsecutiveRoles(JsonArray messages) {
		if (messages.Count <= 1) return messages;

		JsonArray result = [];
		for (int i = 0; i < messages.Count; i++) {
			JsonObject current = (JsonObject)messages[i]!.DeepClone();
			string currentRole = current["role"]!.GetValue<string>();

			// Look ahead for consecutive messages with the same role
			while (i + 1 < messages.Count
			       && messages[i + 1] is JsonObject next
			       && next["role"]?.GetValue<string>() == currentRole) {
				i++;
				MergeContent(current, (JsonObject)next.DeepClone());
			}

			result.Add(current);
		}

		return result;
	}

	/// <summary>
	/// Merges the content of <paramref name="source"/> into <paramref name="target"/>.
	/// Ensures both end up using the content array format.
	/// </summary>
	private static void MergeContent(JsonObject target, JsonObject source) {
		JsonArray targetArr = EnsureContentArray(target);
		JsonArray sourceArr = EnsureContentArray(source);
		foreach (JsonNode? item in sourceArr)
			if (item is not null)
				targetArr.Add(item.DeepClone());
	}

	private static JsonArray EnsureContentArray(JsonObject msg) {
		JsonNode? content = msg["content"];

		if (content is JsonArray arr)
			return arr;

		// Convert string content to array format
		string text = content?.GetValue<string>() ?? "";
		JsonArray newArr = [new JsonObject { ["type"] = "text", ["text"] = text }];
		msg["content"] = newArr;
		return newArr;
	}
}
