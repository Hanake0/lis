using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for session folder and chat-pin changes.</summary>
public static class FolderEventNames {
	public const string Created     = "folder_created";
	public const string Updated     = "folder_updated";
	public const string Deleted     = "folder_deleted";
}

public static class ChatPinEventNames {
	public const string Pinned   = "chat_pinned";
	public const string Unpinned = "chat_unpinned";
}

public sealed record FolderCreatedPayload(
	[property: JsonPropertyName("folder")] object Folder
);

public sealed record FolderUpdatedPayload(
	[property: JsonPropertyName("folder")] object Folder
);

public sealed record FolderDeletedPayload(
	[property: JsonPropertyName("folder_id")] string FolderId
);

public sealed record ChatPinnedPayload(
	[property: JsonPropertyName("chat_id")]    string         ChatId,
	[property: JsonPropertyName("pinned_at")]  DateTimeOffset PinnedAt
);

public sealed record ChatUnpinnedPayload(
	[property: JsonPropertyName("chat_id")] string ChatId
);
