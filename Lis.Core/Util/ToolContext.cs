using Lis.Core.Channel;

namespace Lis.Core.Util;

public static class ToolContext {
	private static readonly AsyncLocal<string?>         ChatIdLocal  = new();
	private static readonly AsyncLocal<IChannelClient?> ChannelLocal = new();

	public static string?         ChatId  { get => ChatIdLocal.Value;  set => ChatIdLocal.Value = value; }
	public static IChannelClient? Channel { get => ChannelLocal.Value; set => ChannelLocal.Value = value; }

	public static bool NotificationsEnabled =>
		Environment.GetEnvironmentVariable("LIS_TOOL_NOTIFICATIONS") == "true";

	public static async Task NotifyAsync(string message, CancellationToken ct = default) {
		if (!NotificationsEnabled || ChatId is null || Channel is null) return;
		await Channel.SendMessageAsync(ChatId, message, null, ct);
	}
}
