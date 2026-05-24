using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for project changes.</summary>
public static class ProjectEventNames {
	public const string Created     = "project_created";
	public const string Updated     = "project_updated";
	public const string Deleted     = "project_deleted";
	public const string ChatMoved   = "chat_moved";
}

public sealed record ProjectCreatedPayload(
	[property: JsonPropertyName("project")] object Project
);

public sealed record ProjectUpdatedPayload(
	[property: JsonPropertyName("project")] object Project
);

public sealed record ProjectDeletedPayload(
	[property: JsonPropertyName("project_id")] string ProjectId
);

public sealed record ChatMovedPayload(
	[property: JsonPropertyName("chat_id")]    string  ChatId,
	[property: JsonPropertyName("project_id")] string? ProjectId
);
