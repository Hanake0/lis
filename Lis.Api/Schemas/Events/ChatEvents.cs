using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for chat lifecycle changes.</summary>
public static class ChatEventNames {
	public const string Created   = "chat_created";
	public const string Updated   = "chat_updated";
	public const string Deleted   = "chat_deleted";
	public const string Archived  = "chat_archived";
	public const string Pinned    = "chat_pinned";
	public const string Unpinned  = "chat_unpinned";
}

public sealed record ChatCreatedPayload(
	[property: JsonPropertyName("chat")] object Chat
);

public sealed record ChatUpdatedPayload(
	[property: JsonPropertyName("chat")] object Chat
);

public sealed record ChatDeletedPayload(
	[property: JsonPropertyName("chat_id")] string ChatId
);
