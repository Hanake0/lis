using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for git-state changes.</summary>
public static class GitEventNames {
	public const string StatusChanged = "git_status_changed";
}

public sealed record GitStatusChangedPayload(
	[property: JsonPropertyName("path")] string Path
);
