using System.Text.Json.Serialization;

namespace Lis.Core.Events;

/// <summary>
/// A server-sent event payload. <see cref="Name"/> goes on the SSE <c>event:</c> line,
/// <see cref="Data"/> is serialised to JSON on the <c>data:</c> line, and <see cref="Id"/>
/// is the monotonic identifier used for <c>Last-Event-ID</c> replay.
/// </summary>
public sealed record ServerEvent(
	[property: JsonPropertyName("id")]    long   Id,
	[property: JsonPropertyName("event")] string Name,
	[property: JsonPropertyName("data")]  object Data,
	[property: JsonPropertyName("ts")]    DateTimeOffset Timestamp
);
