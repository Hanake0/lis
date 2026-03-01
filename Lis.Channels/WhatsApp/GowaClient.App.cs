using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient {

	// ── App ──────────────────────────────────────────────────────────

	[Trace("GowaClient > LoginAsync")]
	public async Task<LoginResult?> LoginAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/app/login", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<LoginResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<LoginResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LoginWithCodeAsync")]
	public async Task<LoginWithCodeResult?> LoginWithCodeAsync(string phone, CancellationToken ct = default) {
		string url = $"/api/app/login-with-code?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<LoginWithCodeResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<LoginWithCodeResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LogoutAsync")]
	public async Task LogoutAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/app/logout", ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > ReconnectAsync")]
	public async Task ReconnectAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/app/reconnect", ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > GetStatusAsync")]
	public async Task<DeviceStatus?> GetStatusAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/app/status", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<DeviceStatus>? result = await response.Content.ReadFromJsonAsync<GowaResponse<DeviceStatus>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > GetDevicesAsync")]
	public async Task<DeviceInfo[]?> GetDevicesAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/app/devices", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<DeviceInfo[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<DeviceInfo[]>>(ct);
		return result?.Results;
	}

	// ── Device Management ────────────────────────────────────────────

	[Trace("GowaClient > ListDevicesAsync")]
	public async Task<DeviceInfo[]?> ListDevicesAsync(CancellationToken ct = default) {
		HttpResponseMessage response = await httpClient.GetAsync("/api/devices", ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<DeviceInfo[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<DeviceInfo[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > AddDeviceAsync")]
	public async Task AddDeviceAsync(string? deviceId = null, CancellationToken ct = default) {
		var payload = new { device_id = deviceId };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/devices", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > GetDeviceInfoAsync")]
	public async Task<DeviceInfo?> GetDeviceInfoAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<DeviceInfo>? result = await response.Content.ReadFromJsonAsync<GowaResponse<DeviceInfo>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > RemoveDeviceAsync")]
	public async Task RemoveDeviceAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}";
		HttpResponseMessage response = await httpClient.DeleteAsync(url, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > LoginDeviceAsync")]
	public async Task<LoginResult?> LoginDeviceAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/login";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<LoginResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<LoginResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LoginDeviceWithCodeAsync")]
	public async Task<LoginWithCodeResult?> LoginDeviceWithCodeAsync(string deviceId, string phone, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/login/code?phone={Uri.EscapeDataString(phone)}";
		HttpResponseMessage response = await httpClient.PostAsync(url, null, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<LoginWithCodeResult>? result = await response.Content.ReadFromJsonAsync<GowaResponse<LoginWithCodeResult>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LogoutDeviceAsync")]
	public async Task LogoutDeviceAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/logout";
		HttpResponseMessage response = await httpClient.PostAsync(url, null, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > ReconnectDeviceAsync")]
	public async Task ReconnectDeviceAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/reconnect";
		HttpResponseMessage response = await httpClient.PostAsync(url, null, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > GetDeviceStatusAsync")]
	public async Task<DeviceStatus?> GetDeviceStatusAsync(string deviceId, CancellationToken ct = default) {
		string url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/status";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<DeviceStatus>? result = await response.Content.ReadFromJsonAsync<GowaResponse<DeviceStatus>>(ct);
		return result?.Results;
	}
}
