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
	public string? MediaType { get; init; }
	public string? MediaCaption { get; init; }
	public string? MediaPath { get; init; }

	/// <summary>Set after ingestion — the DB-generated primary key.</summary>
	public long DbId { get; set; }
}
