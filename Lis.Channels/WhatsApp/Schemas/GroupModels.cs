using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class GroupInfo {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("topic")]
	public string? Topic { get; init; }

	[JsonPropertyName("is_locked")]
	public bool IsLocked { get; init; }

	[JsonPropertyName("is_announce")]
	public bool IsAnnounce { get; init; }

	[JsonPropertyName("participants")]
	public GroupParticipant[]? Participants { get; init; }
}

public sealed class GroupParticipant {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("is_admin")]
	public bool IsAdmin { get; init; }

	[JsonPropertyName("is_super_admin")]
	public bool IsSuperAdmin { get; init; }
}

public sealed class ManageParticipantsRequest {
	[JsonPropertyName("group_id")]
	public required string GroupId { get; init; }

	[JsonPropertyName("participants")]
	public required string[] Participants { get; init; }
}

public sealed class GroupInviteLink {
	[JsonPropertyName("link")]
	public string? Link { get; init; }
}

public sealed class GroupJoinRequestInfo {
	[JsonPropertyName("jid")]
	public string? Jid { get; init; }

	[JsonPropertyName("reason")]
	public string? Reason { get; init; }

	[JsonPropertyName("request_time")]
	public long RequestTime { get; init; }
}
