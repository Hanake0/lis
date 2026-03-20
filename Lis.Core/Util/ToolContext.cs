using Lis.Core.Channel;

namespace Lis.Core.Util;

public static class ToolContext {
	private static readonly AsyncLocal<string?>         ChatIdLocal          = new();
	private static readonly AsyncLocal<IChannelClient?> ChannelLocal         = new();
	private static readonly AsyncLocal<bool>            NotificationsLocal   = new();
	private static readonly AsyncLocal<string?>         MessageExternalIdLocal = new();
	private static readonly AsyncLocal<int>             CacheBreakLocal      = new();
	private static readonly AsyncLocal<long?>           AgentIdLocal         = new();
	private static readonly AsyncLocal<string?>         SenderJidLocal       = new();
	private static readonly AsyncLocal<bool>            IsOwnerLocal         = new();

	public static string?         ChatId               { get => ChatIdLocal.Value;        set => ChatIdLocal.Value = value; }
	public static IChannelClient? Channel              { get => ChannelLocal.Value;        set => ChannelLocal.Value = value; }
	public static bool            NotificationsEnabled { get => NotificationsLocal.Value;  set => NotificationsLocal.Value = value; }
	public static string?         MessageExternalId    { get => MessageExternalIdLocal.Value; set => MessageExternalIdLocal.Value = value; }

	/// <summary>
	/// Index (in the messages array, 0-based, excluding system) of the last message
	/// at or before the tool prune boundary. -1 if no pruning is active.
	/// Set by ContextWindowBuilder, read by CacheControlHandler.
	/// </summary>
	public static int             CacheBreakIndex      { get => CacheBreakLocal.Value;    set => CacheBreakLocal.Value = value; }

	public static long?           AgentId              { get => AgentIdLocal.Value;       set => AgentIdLocal.Value = value; }
	public static string?         SenderJid            { get => SenderJidLocal.Value;     set => SenderJidLocal.Value = value; }
	public static bool            IsOwner              { get => IsOwnerLocal.Value;       set => IsOwnerLocal.Value = value; }

	public static async Task NotifyAsync(string message, CancellationToken ct = default) {
		if (!NotificationsEnabled || ChatId is null || Channel is null) return;
		await Channel.SendMessageAsync(ChatId, message, null, ct);
	}
}
