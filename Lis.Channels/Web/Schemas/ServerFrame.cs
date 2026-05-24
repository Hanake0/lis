using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

/// <summary>
/// Server → client WebSocket frame. Flat shape; <see cref="Type"/> is the discriminator.
/// Null fields are omitted on serialization (see JsonOpt).
/// </summary>
public sealed class ServerFrame {
	[JsonPropertyName("type")]
	public required string Type { get; init; }

	[JsonPropertyName("chat_id")]
	public string? ChatId { get; init; }

	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	[JsonPropertyName("role")]
	public string? Role { get; init; }

	[JsonPropertyName("timestamp")]
	public DateTimeOffset? Timestamp { get; init; }

	[JsonPropertyName("emoji")]
	public string? Emoji { get; init; }

	[JsonPropertyName("error")]
	public string? ErrorMessage { get; init; }

	[JsonPropertyName("chats")]
	public IReadOnlyList<ChatSummary>? Chats { get; init; }

	[JsonPropertyName("messages")]
	public IReadOnlyList<MessageRow>? Messages { get; init; }

	public static ServerFrame Connected() =>
		new() { Type = "connected" };

	public static ServerFrame Message(string chatId, string id, string text, string role, DateTimeOffset timestamp) =>
		new() { Type = "message", ChatId = chatId, Id = id, Text = text, Role = role, Timestamp = timestamp };

	public static ServerFrame Typing(string chatId) =>
		new() { Type = "typing", ChatId = chatId };

	public static ServerFrame TypingStopped(string chatId) =>
		new() { Type = "typing_stopped", ChatId = chatId };

	public static ServerFrame Reaction(string chatId, string messageId, string emoji) =>
		new() { Type = "reaction", ChatId = chatId, Id = messageId, Emoji = emoji };

	public static ServerFrame Error(string error, string? chatId = null) =>
		new() { Type = "error", ErrorMessage = error, ChatId = chatId };

	public static ServerFrame ChatList(IReadOnlyList<ChatSummary> chats) =>
		new() { Type = "chat_list", Chats = chats };

	public static ServerFrame ChatHistory(string chatId, IReadOnlyList<MessageRow> messages) =>
		new() { Type = "chat_history", ChatId = chatId, Messages = messages };
}
