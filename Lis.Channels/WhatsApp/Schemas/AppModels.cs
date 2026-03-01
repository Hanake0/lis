using System.Text.Json.Serialization;

namespace Lis.Channels.WhatsApp.Schemas;

public sealed class LoginResult {
	[JsonPropertyName("qr_duration")]
	public int QrDuration { get; init; }

	[JsonPropertyName("image_path")]
	public string? ImagePath { get; init; }
}

public sealed class LoginWithCodeResult {
	[JsonPropertyName("code")]
	public string? Code { get; init; }
}

public sealed class DeviceStatus {
	[JsonPropertyName("is_connected")]
	public bool IsConnected { get; init; }

	[JsonPropertyName("is_logged_in")]
	public bool IsLoggedIn { get; init; }

	[JsonPropertyName("device_id")]
	public string? DeviceId { get; init; }
}

public sealed class DeviceInfo {
	[JsonPropertyName("device_id")]
	public string? DeviceId { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("status")]
	public string? Status { get; init; }
}
