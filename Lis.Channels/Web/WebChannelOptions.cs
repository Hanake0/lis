namespace Lis.Channels.Web;

public sealed class WebChannelOptions {
	/// <summary>Shared secret required on the WS upgrade query string (?token=...). Connection is rejected if empty or mismatched.</summary>
	public required string AuthToken { get; init; }

	/// <summary>Sender id used for messages arriving on this channel (acts as the owner JID for auth purposes).</summary>
	public required string OwnerId { get; init; }

	/// <summary>Display name attached to inbound messages.</summary>
	public string? OwnerName { get; init; }

	/// <summary>Prefix used when minting new chat ids server-side (clients can also mint their own).</summary>
	public string ChatIdPrefix { get; init; } = "web:";
}
