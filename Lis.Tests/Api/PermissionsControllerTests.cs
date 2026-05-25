using Lis.Agent;
using Lis.Api.Controllers;
using Lis.Api.Schemas.Approvals;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="PermissionsController"/>. A capturing
/// <see cref="IApprovalService"/> records the (id, decision, sender) tuple so
/// we can assert the parse and the channel-marker behaviour.
/// </summary>
public class PermissionsControllerTests {
	private readonly CapturingApprovalService approvals = new();
	private readonly PermissionsController    sut;

	public PermissionsControllerTests() {
		this.sut = new PermissionsController(this.approvals);
	}

	[Theory]
	[InlineData("once",   ApprovalDecision.Once)]
	[InlineData("always", ApprovalDecision.Always)]
	[InlineData("deny",   ApprovalDecision.Deny)]
	public async Task Respond_Parses_The_Three_Canonical_Decisions(string wire, ApprovalDecision expected) {
		this.approvals.NextReturn = true;
		IActionResult result = await this.sut.Respond("appr-1", new RespondToApprovalRequest(wire), default);
		Assert.IsType<NoContentResult>(result);
		(string id, ApprovalDecision decision, string sender) = Assert.Single(this.approvals.Calls);
		Assert.Equal("appr-1",  id);
		Assert.Equal(expected,  decision);
		Assert.Equal("web",     sender);
	}

	[Fact]
	public async Task Respond_Folds_Unrecognised_Decision_To_Deny_Matching_The_Legacy_WS_Frame() {
		this.approvals.NextReturn = true;
		await this.sut.Respond("appr-1", new RespondToApprovalRequest("garbage"), default);
		Assert.Equal(ApprovalDecision.Deny, this.approvals.Calls[0].decision);
	}

	[Fact]
	public async Task Respond_Missing_Approval_Throws_KeyNotFoundException() {
		this.approvals.NextReturn = false;
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Respond("appr-ghost", new RespondToApprovalRequest("deny"), default));
	}

	private sealed class CapturingApprovalService :IApprovalService {
		public List<(string id, ApprovalDecision decision, string sender)> Calls { get; } = [];
		public bool NextReturn { get; set; }

		public Task<bool> ResolveAsync(string approvalId, ApprovalDecision decision, string senderJid) {
			this.Calls.Add((approvalId, decision, senderJid));
			return Task.FromResult(this.NextReturn);
		}

		public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
			=> throw new NotSupportedException("not used by controller tests");

		public Task<bool> ResolveByMessageAsync(string messageExternalId, ApprovalDecision decision, string senderJid)
			=> throw new NotSupportedException("not used by controller tests");
	}
}
