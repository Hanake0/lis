using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Approvals;

/// <summary>
/// Response to a pending tool-approval prompt. <c>decision</c> is one of
/// <c>once</c> (allow this call only), <c>always</c> (allow + remember the
/// allowance for the chat), <c>deny</c> (refuse + remember). Anything else
/// is coerced to <c>deny</c> server-side — matches the legacy WS frame's
/// permissive parsing.
/// </summary>
public sealed record RespondToApprovalRequest(
	[property: JsonPropertyName("decision"), Required, MaxLength(16)] string Decision
);
