using System.Diagnostics;
using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient(HttpClient httpClient) {

	// ── Send Message ─────────────────────────────────────────────────

	[Trace("GowaClient > SendMessageAsync")]
	public async Task<SendResult?> SendMessageAsync(
		string phone, string message, string? replyMessageId = null,
		bool isForwarded = false, int? duration = null, string[]? mentions = null,
		CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		SendMessageRequest request = new() {
			Phone          = phone,
			Message        = message,
			ReplyMessageId = replyMessageId,
			IsForwarded    = isForwarded ? true : null,
			Duration       = duration,
			Mentions       = mentions,
		};

		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/message", request, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Image ───────────────────────────────────────────────────

	[Trace("GowaClient > SendImageAsync")]
	public async Task<SendResult?> SendImageAsync(
		string phone, string imageUrl, string? caption = null, bool viewOnce = false,
		bool compress = false, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, image_url = imageUrl, caption, view_once = viewOnce, compress };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/image", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > SendImageAsync (upload)")]
	public async Task<SendResult?> SendImageAsync(
		string phone, Stream image, string fileName, string? caption = null,
		bool viewOnce = false, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(phone), "phone");
		content.Add(new StreamContent(image), "image", fileName);
		if (caption is not null) content.Add(new StringContent(caption), "caption");
		if (viewOnce) content.Add(new StringContent("true"), "view_once");

		HttpResponseMessage response = await httpClient.PostAsync("/api/send/image", content, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Audio ───────────────────────────────────────────────────

	[Trace("GowaClient > SendAudioAsync")]
	public async Task<SendResult?> SendAudioAsync(
		string phone, string audioUrl, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, audio_url = audioUrl };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/audio", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > SendAudioAsync (upload)")]
	public async Task<SendResult?> SendAudioAsync(
		string phone, Stream audio, string fileName, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(phone), "phone");
		content.Add(new StreamContent(audio), "audio", fileName);

		HttpResponseMessage response = await httpClient.PostAsync("/api/send/audio", content, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send File ────────────────────────────────────────────────────

	[Trace("GowaClient > SendFileAsync")]
	public async Task<SendResult?> SendFileAsync(
		string phone, Stream file, string fileName, string? caption = null,
		CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(phone), "phone");
		content.Add(new StreamContent(file), "file", fileName);
		if (caption is not null) content.Add(new StringContent(caption), "caption");

		HttpResponseMessage response = await httpClient.PostAsync("/api/send/file", content, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Sticker ─────────────────────────────────────────────────

	[Trace("GowaClient > SendStickerAsync")]
	public async Task<SendResult?> SendStickerAsync(
		string phone, string stickerUrl, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, sticker_url = stickerUrl };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/sticker", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > SendStickerAsync (upload)")]
	public async Task<SendResult?> SendStickerAsync(
		string phone, Stream sticker, string fileName, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(phone), "phone");
		content.Add(new StreamContent(sticker), "sticker", fileName);

		HttpResponseMessage response = await httpClient.PostAsync("/api/send/sticker", content, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Video ───────────────────────────────────────────────────

	[Trace("GowaClient > SendVideoAsync")]
	public async Task<SendResult?> SendVideoAsync(
		string phone, string videoUrl, string? caption = null, bool viewOnce = false,
		bool compress = false, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, video_url = videoUrl, caption, view_once = viewOnce, compress };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/video", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > SendVideoAsync (upload)")]
	public async Task<SendResult?> SendVideoAsync(
		string phone, Stream video, string fileName, string? caption = null,
		bool viewOnce = false, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(phone), "phone");
		content.Add(new StreamContent(video), "video", fileName);
		if (caption is not null) content.Add(new StringContent(caption), "caption");
		if (viewOnce) content.Add(new StringContent("true"), "view_once");

		HttpResponseMessage response = await httpClient.PostAsync("/api/send/video", content, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Contact ─────────────────────────────────────────────────

	[Trace("GowaClient > SendContactAsync")]
	public async Task<SendResult?> SendContactAsync(
		string phone, string contactName, string contactPhone, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, contact_name = contactName, contact_phone = contactPhone };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/contact", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Link ────────────────────────────────────────────────────

	[Trace("GowaClient > SendLinkAsync")]
	public async Task<SendResult?> SendLinkAsync(
		string phone, string link, string? caption = null, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, link, caption };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/link", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Location ────────────────────────────────────────────────

	[Trace("GowaClient > SendLocationAsync")]
	public async Task<SendResult?> SendLocationAsync(
		string phone, string latitude, string longitude, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", phone);

		var payload = new { phone, latitude, longitude };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/location", payload, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Send Poll ────────────────────────────────────────────────────

	[Trace("GowaClient > SendPollAsync")]
	public async Task<SendResult?> SendPollAsync(SendPollRequest request, CancellationToken ct = default) {
		Activity.Current?.SetTag("chat.phone", request.Phone);

		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/send/poll", request, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<SendResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<SendResult>>(ct);
		return result?.Results;
	}

	// ── Presence ─────────────────────────────────────────────────────

	[Trace("GowaClient > SendChatPresenceAsync")]
	public async Task SendChatPresenceAsync(string phone, string action, CancellationToken ct = default) {
		var payload = new { phone, action };
		await httpClient.PostAsJsonAsync("/api/send/chat-presence", payload, ct);
	}

	[Trace("GowaClient > SendPresenceAsync")]
	public async Task SendPresenceAsync(string type, CancellationToken ct = default) {
		var payload = new { type };
		await httpClient.PostAsJsonAsync("/api/send/presence", payload, ct);
	}
}
