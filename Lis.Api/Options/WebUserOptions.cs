namespace Lis.Api.Options;

/// <summary>
/// Identifies the human owner of the web-channel REST surface. Populated from
/// the same env vars the legacy <c>WebChannelOptions</c> used so existing
/// deployments don't need a config change during the WS→REST teardown.
///
/// Used by <see cref="Lis.Api.Controllers.ChatsController"/> when stamping
/// inbound user messages (sender id / display name on the persisted row).
/// </summary>
public sealed class WebUserOptions {
	/// <summary>Sender id used for messages arriving on this channel (acts as the owner JID for auth purposes).</summary>
	public required string OwnerId { get; init; }

	/// <summary>Display name attached to inbound messages. Optional.</summary>
	public string? OwnerName { get; init; }
}
