using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

/// <summary>
/// Client → server WebSocket frame. Flat shape; <see cref="Type"/> is the discriminator.
/// </summary>
public sealed class ClientFrame {
	[JsonPropertyName("type")]
	public string? Type { get; init; }

	[JsonPropertyName("chat_id")]
	public string? ChatId { get; init; }

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	/// <summary>One of: "default", "immediate", "queue_only", "interrupt". Defaults to "immediate" for web.</summary>
	[JsonPropertyName("dispatch")]
	public string? Dispatch { get; init; }

	[JsonPropertyName("limit")]
	public int? Limit { get; init; }

	// Per-message overrides
	[JsonPropertyName("model")]
	public string? Model { get; init; }

	[JsonPropertyName("effort")]
	public string? Effort { get; init; }

	[JsonPropertyName("tool_profile")]
	public string? ToolProfile { get; init; }

	/// <summary>One of: "budget", "adaptive", "auto". Forces the thinking config shape.</summary>
	[JsonPropertyName("thinking_mode")]
	public string? ThinkingMode { get; init; }

	// Image attachments (base64 data URLs)
	[JsonPropertyName("attachments")]
	public List<ClientImageAttachment>? Attachments { get; init; }

	// Session management
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	// Agent management
	[JsonPropertyName("agent_name")]
	public string? AgentName { get; init; }

	// get_context — bypass the in-memory counter cache and force a fresh
	// count_tokens roundtrip for every text-to-token conversion.
	[JsonPropertyName("force_refresh")]
	public bool? ForceRefresh { get; init; }

	// Permission response
	[JsonPropertyName("approval_id")]
	public string? ApprovalId { get; init; }

	/// <summary>One of: "once", "always", "deny".</summary>
	[JsonPropertyName("decision")]
	public string? Decision { get; init; }

	// Question response
	[JsonPropertyName("question_id")]
	public string? QuestionId { get; init; }

	/// <summary>Array of arrays — one answer set per question in the original request.</summary>
	[JsonPropertyName("answers")]
	public List<List<string>>? Answers { get; init; }

	/// <summary>True when the user dismissed without answering.</summary>
	[JsonPropertyName("rejected")]
	public bool? Rejected { get; init; }
}
