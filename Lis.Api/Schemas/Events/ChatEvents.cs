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

/// <summary>
/// Emitted when a chat is archived (chat row keeps its data; <c>archived_at</c>
/// gets set so the sidebar moves it into its archived bucket). The full DTO is
/// included so consumers don't need a follow-up GET.
/// </summary>
public sealed record ChatArchivedPayload(
	[property: JsonPropertyName("chat")] object Chat
);
