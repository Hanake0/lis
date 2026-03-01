using System.Diagnostics;
using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient {

	[Trace("GowaClient > GetUserInfoAsync")]
	public async Task<UserInfo?> GetUserInfoAsync(string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("user.phone", phone);

		string url = $"/user/info?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserInfo>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserInfo>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetUserAvatarAsync")]
	public async Task<UserAvatar?> GetUserAvatarAsync(string phone, bool isPreview = true, CancellationToken ct = default) {
		Activity.Current?.SetTag("user.phone", phone);

		string url = $"/user/avatar?phone={Uri.EscapeDataString(phone)}&is_preview={isPreview.ToString().ToLowerInvariant()}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserAvatar>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserAvatar>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > SetUserAvatarAsync")]
	public async Task SetUserAvatarAsync(Stream avatar, string fileName, CancellationToken ct = default) {
		using MultipartFormDataContent content = new();
		content.Add(new StreamContent(avatar), "avatar", fileName);

		HttpResponseMessage response = await httpClient.PostAsync("/user/avatar", content, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetPushNameAsync")]
	public async Task SetPushNameAsync(string pushName, CancellationToken ct = default) {
		var payload = new { push_name = pushName };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/user/pushname", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > GetMyPrivacyAsync")]
	public async Task<UserPrivacy?> GetMyPrivacyAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/user/my/privacy", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserPrivacy>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserPrivacy>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetMyGroupsAsync")]
	public async Task<UserGroup[]?> GetMyGroupsAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/user/my/groups", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserGroup[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserGroup[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetMyNewslettersAsync")]
	public async Task<GowaResponse<object>?> GetMyNewslettersAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/user/my/newsletters", ct);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<GowaResponse<object>>(ct);
	}

	[Trace("GowaClient > GetMyContactsAsync")]
	public async Task<UserContact[]?> GetMyContactsAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/user/my/contacts", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserContact[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserContact[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > CheckUserAsync")]
	public async Task<UserCheckResult?> CheckUserAsync(string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("user.phone", phone);

		string url = $"/user/check?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<UserCheckResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<UserCheckResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetBusinessProfileAsync")]
	public async Task<BusinessProfile?> GetBusinessProfileAsync(string phone, CancellationToken ct = default) {
		Activity.Current?.SetTag("user.phone", phone);

		string url = $"/user/business-profile?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<BusinessProfile>? result = await response.Content.ReadFromJsonAsync<GowaResponse<BusinessProfile>>(ct);
		return result?.Results;
	}
}
