using System.Text.Json.Serialization;

namespace Lis.Core.Events;

/// <summary>
/// A server-sent event payload. <see cref="Name"/> goes on the SSE <c>event:</c> line,
/// <see cref="Data"/> is serialised to JSON on the <c>data:</c> line, and <see cref="Id"/>
/// is the monotonic identifier used for <c>Last-Event-ID</c> replay.
///
/// <see cref="Subject"/> is an optional routing tag for filtered subscriptions —
/// chat-scoped events use <c>chat:{external_id}</c> so the <c>ChatStreamController</c>
/// can subscribe to a single chat's token stream without seeing other chats' traffic.
/// Global events leave it null and reach every <c>/api/events</c> subscriber.
/// </summary>
public sealed record ServerEvent(
	[property: JsonPropertyName("id")]      long           Id,
	[property: JsonPropertyName("event")]   string         Name,
	[property: JsonPropertyName("data")]    object         Data,
	[property: JsonPropertyName("ts")]      DateTimeOffset Timestamp,
	[property: JsonPropertyName("subject")] string?        Subject = null
);
