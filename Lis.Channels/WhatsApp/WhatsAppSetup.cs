using Lis.Core.Channel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lis.Channels.WhatsApp;

public static class WhatsAppSetup {
	public static IServiceCollection AddWhatsApp(this IServiceCollection services) {
		GowaOptions opts = new() {
			BaseUrl       = Env("GOWA_BASE_URL"),
			DeviceId      = Env("GOWA_DEVICE_ID"),
			BasicAuth     = Env("GOWA_BASIC_AUTH"),
			WebhookSecret = Env("GOWA_WEBHOOK_SECRET"),
		};

		services.AddSingleton(Options.Create(opts));
		services.AddSingleton<WebhookValidator>();

		services.AddHttpClient<GowaClient>((sp, client) => {
			client.BaseAddress = new Uri(opts.BaseUrl);
			client.DefaultRequestHeaders.Add("X-Device-Id", opts.DeviceId);

			if (!string.IsNullOrEmpty(opts.BasicAuth)) {
				string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(opts.BasicAuth));
				client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
			}
		}).AddStandardResilienceHandler();

		services.AddSingleton<IMessageFormatter, WhatsAppFormatter>();
		services.AddScoped<IChannelClient, WhatsAppClient>();

		return services;
	}

	private static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? "";
}
