using Lis.Core.Channel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lis.Channels.Web;

public static class WebChannelSetup {
	public static IServiceCollection AddWebChannel(this IServiceCollection services) {
		WebChannelOptions opts = new() {
			AuthToken    = Env("WEB_CHANNEL_TOKEN"),
			OwnerId      = Env("WEB_CHANNEL_OWNER_ID")   is { Length: > 0 } o ? o : "web:owner",
			OwnerName    = Env("WEB_CHANNEL_OWNER_NAME") is { Length: > 0 } n ? n : null,
			ChatIdPrefix = Env("WEB_CHANNEL_CHAT_PREFIX") is { Length: > 0 } p ? p : "web:",
		};

		services.AddSingleton(Options.Create(opts));

		// Same instance serves IChannelClient (publisher) and WebChannelClient (registry for the controller).
		services.AddSingleton<WebChannelClient>();
		services.AddSingleton<IChannelClient>(sp => sp.GetRequiredService<WebChannelClient>());
		services.AddSingleton<IMessageFormatter, WebMessageFormatter>();

		return services;
	}

	private static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? "";
}
