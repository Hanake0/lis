using System.Diagnostics;
using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient {

	[Trace("GowaClient > RevokeMessageAsync")]
	public async Task RevokeMessageAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/revoke", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > DeleteMessageAsync")]
	public async Task DeleteMessageAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/delete", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > ReactToMessageAsync")]
	public async Task ReactToMessageAsync(string messageId, string phone, string emoji, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone, emoji };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/reaction", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > EditMessageAsync")]
	public async Task EditMessageAsync(string messageId, string phone, string message, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone, message };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/update", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > MarkMessageReadAsync")]
	public async Task MarkMessageReadAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/read", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > StarMessageAsync")]
	public async Task StarMessageAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/star", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > UnstarMessageAsync")]
	public async Task UnstarMessageAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		var payload = new { phone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/message/{messageId}/unstar", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > DownloadMediaAsync")]
	public async Task<MediaDownloadResult?> DownloadMediaAsync(string messageId, string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("message.id", messageId);

		string url = $"/message/{messageId}/download?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<MediaDownloadResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<MediaDownloadResult>>(ct);
		return result?.Results;
	}
}
