using System.Diagnostics;
using System.Net.Http.Json;

using Lis.Channels.WhatsApp.Schemas;
using Lis.Core.Util;

namespace Lis.Channels.WhatsApp;

public sealed partial class GowaClient {

	// ── Group Info ───────────────────────────────────────────────────

	[Trace("GowaClient > GetGroupInfoAsync")]
	public async Task<GroupInfo?> GetGroupInfoAsync(string groupId, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		string url = $"/api/group/info?group_id={Uri.EscapeDataString(groupId)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<GroupInfo>? result = await response.Content.ReadFromJsonAsync<GowaResponse<GroupInfo>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > CreateGroupAsync")]
	public async Task CreateGroupAsync(string title, string[] participants, CancellationToken ct = default) {
		var payload = new { title, participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	// ── Participants ─────────────────────────────────────────────────

	[Trace("GowaClient > GetGroupParticipantsAsync")]
	public async Task<GroupParticipant[]?> GetGroupParticipantsAsync(string groupId, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		string url = $"/api/group/participants?group_id={Uri.EscapeDataString(groupId)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<GroupParticipant[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<GroupParticipant[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > AddGroupParticipantsAsync")]
	public async Task AddGroupParticipantsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		ManageParticipantsRequest request = new() { GroupId = groupId, Participants = participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participants", request, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > RemoveGroupParticipantsAsync")]
	public async Task RemoveGroupParticipantsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		ManageParticipantsRequest request = new() { GroupId = groupId, Participants = participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participants/remove", request, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > PromoteParticipantsAsync")]
	public async Task PromoteParticipantsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		ManageParticipantsRequest request = new() { GroupId = groupId, Participants = participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participants/promote", request, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > DemoteParticipantsAsync")]
	public async Task DemoteParticipantsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		ManageParticipantsRequest request = new() { GroupId = groupId, Participants = participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participants/demote", request, ct);
		response.EnsureSuccessStatusCode();
	}

	// ── Join / Leave ─────────────────────────────────────────────────

	[Trace("GowaClient > JoinGroupAsync")]
	public async Task JoinGroupAsync(string link, CancellationToken ct = default) {
		var payload = new { link };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/join-with-link", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > GetGroupFromLinkAsync")]
	public async Task<GroupInfo?> GetGroupFromLinkAsync(string link, CancellationToken ct = default) {
		string url = $"/api/group/info-from-link?link={Uri.EscapeDataString(link)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<GroupInfo>? result = await response.Content.ReadFromJsonAsync<GowaResponse<GroupInfo>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > LeaveGroupAsync")]
	public async Task LeaveGroupAsync(string groupId, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/leave", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	// ── Join Requests ────────────────────────────────────────────────

	[Trace("GowaClient > GetJoinRequestsAsync")]
	public async Task<GroupJoinRequestInfo[]?> GetJoinRequestsAsync(string groupId, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		string url = $"/api/group/participant-requests?group_id={Uri.EscapeDataString(groupId)}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<GroupJoinRequestInfo[]>? result = await response.Content.ReadFromJsonAsync<GowaResponse<GroupJoinRequestInfo[]>>(ct);
		return result?.Results;
	}

	[Trace("GowaClient > ApproveJoinRequestsAsync")]
	public async Task ApproveJoinRequestsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participant-requests/approve", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > RejectJoinRequestsAsync")]
	public async Task RejectJoinRequestsAsync(string groupId, string[] participants, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, participants };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/participant-requests/reject", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	// ── Group Settings ───────────────────────────────────────────────

	[Trace("GowaClient > SetGroupPhotoAsync")]
	public async Task SetGroupPhotoAsync(string groupId, Stream photo, string fileName, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		using MultipartFormDataContent content = new();
		content.Add(new StringContent(groupId), "group_id");
		content.Add(new StreamContent(photo), "photo", fileName);

		HttpResponseMessage response = await httpClient.PostAsync("/api/group/photo", content, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetGroupNameAsync")]
	public async Task SetGroupNameAsync(string groupId, string name, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, name };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/name", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetGroupLockedAsync")]
	public async Task SetGroupLockedAsync(string groupId, bool locked, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, locked };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/locked", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetGroupAnnounceAsync")]
	public async Task SetGroupAnnounceAsync(string groupId, bool announce, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, announce };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/announce", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	[Trace("GowaClient > SetGroupTopicAsync")]
	public async Task SetGroupTopicAsync(string groupId, string? topic, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		var payload = new { group_id = groupId, topic };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync("/api/group/topic", payload, ct);
		response.EnsureSuccessStatusCode();
	}

	// ── Invite Link ──────────────────────────────────────────────────

	[Trace("GowaClient > GetGroupInviteLinkAsync")]
	public async Task<string?> GetGroupInviteLinkAsync(string groupId, bool reset = false, CancellationToken ct = default) {
		Activity.Current?.SetTag("group.id", groupId);

		string url = $"/api/group/invite-link?group_id={Uri.EscapeDataString(groupId)}&reset={reset.ToString().ToLowerInvariant()}";
		HttpResponseMessage response = await httpClient.GetAsync(url, ct);
		response.EnsureSuccessStatusCode();

		GowaResponse<GroupInviteLink>? result = await response.Content.ReadFromJsonAsync<GowaResponse<GroupInviteLink>>(ct);
		return result?.Results?.Link;
	}
}
