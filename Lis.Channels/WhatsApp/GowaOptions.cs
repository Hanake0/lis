namespace Lis.Channels.WhatsApp;

public sealed class GowaOptions {
	public required string BaseUrl { get; init; }
	public required string DeviceId { get; init; }
	public string? BasicAuth { get; init; }
	public required string WebhookSecret { get; init; }
}
