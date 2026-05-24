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

	/// <summary>Set after ingestion — the DB-generated primary key.</summary>
	public long DbId { get; set; }
}
