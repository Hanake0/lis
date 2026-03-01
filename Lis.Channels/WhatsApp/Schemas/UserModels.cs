using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class UserInfo {
	[JsonPropertyName("verified_name")]
	public string? VerifiedName { get; init; }

	[JsonPropertyName("status")]
	public string? Status { get; init; }

	[JsonPropertyName("picture_id")]
	public string? PictureId { get; init; }

	[JsonPropertyName("devices")]
	public UserDevice[]? Devices { get; init; }
}

public sealed class UserDevice {
	[JsonPropertyName("device")]
	public string? Device { get; init; }
}

public sealed class UserAvatar {
	[JsonPropertyName("url")]
	public string? Url { get; init; }

	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("type")]
	public string? Type { get; init; }
}

public sealed class UserPrivacy {
	[JsonPropertyName("group_add")]
	public string? GroupAdd { get; init; }

	[JsonPropertyName("last_seen")]
	public string? LastSeen { get; init; }

	[JsonPropertyName("status")]
	public string? Status { get; init; }

	[JsonPropertyName("profile")]
	public string? Profile { get; init; }

	[JsonPropertyName("read_receipts")]
	public string? ReadReceipts { get; init; }
}

public sealed class UserGroup {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("topic")]
	public string? Topic { get; init; }
}

public sealed class UserContact {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("phone")]
	public string? Phone { get; init; }
}

public sealed class UserCheckResult {
	[JsonPropertyName("is_registered")]
	public bool IsRegistered { get; init; }

	[JsonPropertyName("jid")]
	public string? Jid { get; init; }
}

public sealed class BusinessProfile {
	[JsonPropertyName("address")]
	public string? Address { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("email")]
	public string? Email { get; init; }

	[JsonPropertyName("category")]
	public string? Category { get; init; }

	[JsonPropertyName("website")]
	public string[]? Website { get; init; }
}
