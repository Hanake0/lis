using System.Diagnostics;
using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient {

	[Trace("GowaClient > GetChatsAsync")]
	public async Task<ChatInfo[]?> GetChatsAsync(int limit = 25, int offset = 0, string? search = null, bool? hasMedia = null, CancellationToken ct = default) {
		string url = $"/chats?limit={limit}&offset={offset}";
		if (search is not null) url += $"&search={Uri.EscapeDataString(search)}";
		if (hasMedia is not null) url += $"&has_media={hasMedia.Value.ToString().ToLowerInvariant()}";

		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<ChatInfo[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<ChatInfo[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetChatMessagesAsync")]
	public async Task<ChatMessage[]?> GetChatMessagesAsync(string chatJid, int limit = 50, int offset = 0, string? search = null, bool? mediaOnly = null, bool? isFromMe = null, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.id", chatJid);

		string url = $"/chat/{chatJid}/messages?limit={limit}&offset={offset}";
		if (search is not null) url += $"&search={Uri.EscapeDataString(search)}";
		if (mediaOnly is not null) url += $"&media_only={mediaOnly.Value.ToString().ToLowerInvariant()}";
		if (isFromMe is not null) url += $"&is_from_me={isFromMe.Value.ToString().ToLowerInvariant()}";

		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<ChatMessage[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<ChatMessage[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LabelChatAsync")]
	public async Task LabelChatAsync(string chatJid, string labelId, string labelName, bool labeled, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.id", chatJid);

		var payload = new { label_id = labelId, label_name = labelName, labeled };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/chat/{chatJid}/label", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > PinChatAsync")]
	public async Task PinChatAsync(string chatJid, bool pinned, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.id", chatJid);

		var payload = new { pinned };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/chat/{chatJid}/pin", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetDisappearingAsync")]
	public async Task SetDisappearingAsync(string chatJid, int timerSeconds, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.id", chatJid);

		var payload = new { timer_seconds = timerSeconds };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/chat/{chatJid}/disappearing", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > ArchiveChatAsync")]
	public async Task ArchiveChatAsync(string chatJid, bool archived, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.id", chatJid);

		var payload = new { archived };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/chat/{chatJid}/archive", payload, ct);
		response.EnsureSuccessStatusCode();
	}
}
