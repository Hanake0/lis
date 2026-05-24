using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for multi-run progress.</summary>
public static class MultiRunEventNames {
	public const string Progress = "multi_run_progress";
}

public sealed record MultiRunProgressPayload(
	[property: JsonPropertyName("run_id")]     string  RunId,
	[property: JsonPropertyName("chat_id")]    string  ChatId,
	[property: JsonPropertyName("status")]     string  Status,
	[property: JsonPropertyName("error")]      string? Error
);
