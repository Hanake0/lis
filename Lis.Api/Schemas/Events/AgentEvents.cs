using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for agent CRUD + overrides.</summary>
public static class AgentEventNames {
	public const string Updated = "agent_updated";
}

/// <summary>
/// Fired when an agent's settings change — typically because a chat owner edited
/// the model/effort/tool-profile through the chat settings dialog (which routes
/// through <c>PATCH /api/chats/{id}/overrides</c>) or directly via the agents
/// editor. Consumers update their cached agent rows and re-derive any UI that
/// depended on the changed field (e.g. "model" label in the chat header).
/// </summary>
public sealed record AgentUpdatedPayload(
	[property: JsonPropertyName("agent")] object Agent
);
