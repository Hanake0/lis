namespace Lis.Core.Channel;

public sealed class IncomingMessage {
	public required string ExternalId { get; init; }
	public required string ChatId { get; init; }
	public required string SenderId { get; init; }
	public string? SenderName { get; init; }
	public DateTimeOffset Timestamp { get; init; }
	public bool IsFromMe { get; init; }
	public bool IsGroup { get; init; }
	public string? Body { get; init; }
	public string? RepliedId { get; init; }
	public string? RepliedContent { get; init; }
	public bool IsBotMentioned { get; set; }
	public string? ChatName { get; set; }
	public string? ChatTopic { get; set; }
	public string? MediaType { get; init; }
	public string? MediaCaption { get; init; }
	public string? MediaPath { get; init; }

	/// <summary>Pre-loaded media bytes (e.g. inline image from the web channel). Skips the download path.</summary>
	public byte[]? InlineMediaData { get; init; }
	/// <summary>MIME type for <see cref="InlineMediaData"/>.</summary>
	public string? InlineMediaMimeType { get; init; }

	/// <summary>Source channel identifier (e.g. "web", "whatsapp"). Persisted on new chats.</summary>
	public string? Channel { get; init; }

	/// <summary>How the channel wants this message dispatched. Defaults to debounce (WhatsApp behavior).</summary>
	public MessageDispatchMode Dispatch { get; init; } = MessageDispatchMode.Default;

	// Per-message overrides (web channel only — override agent defaults for this turn)
	public string? ModelOverride        { get; init; }
	public string? EffortOverride       { get; init; }
	public string? ToolProfileOverride  { get; init; }
	public string? ThinkingModeOverride { get; init; } // "budget" | "adaptive" | "auto"

	/// <summary>
	/// Project external_id this chat should be bound to. Honoured only on lazy
	/// chat creation (the first message of a draft) — subsequent messages
	/// ignore it. The webview composer sends this as part of the draft envelope.
	/// </summary>
	public string? ProjectExternalIdHint { get; init; }

	/// <summary>
	/// Per-chat working directory the agent's tools should treat as cwd.
	/// Honoured only on lazy chat creation; subsequent messages ignore it
	/// (use PATCH /api/chats/{id} to change after creation).
	/// </summary>
	public string? DirectoryHint { get; init; }

	/// <summary>Set after ingestion — the DB-generated primary key.</summary>
	public long DbId { get; set; }
}
