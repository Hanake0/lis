using Lis.Agent;
using Lis.Api.Schemas.Approvals;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Resolves pending tool-approval prompts. The agent loop blocks on
/// <see cref="IApprovalService"/> until the user decides; this endpoint is
/// what the chat UI calls to deliver that decision.
///
/// Channel marker on the approval is hard-coded to <c>"web"</c> — the legacy
/// WS frame did the same, and approval auditing groups by channel so all
/// REST-delivered decisions land in the same bucket as the WS ones did.
/// </summary>
[ApiController]
[Route("api/permissions")]
[Tags("Permissions")]
[Produces("application/json")]
public sealed class PermissionsController(IApprovalService approvals) :ControllerBase {

	[HttpPost("{approvalId}/respond")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Respond(
		[FromRoute] string                       approvalId,
		[FromBody]  RespondToApprovalRequest     request,
		CancellationToken                        ct
	) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));
		_ = ct; // hold on to ct for future async services that accept it

		ApprovalDecision decision = request.Decision switch {
			"once"   => ApprovalDecision.Once,
			"always" => ApprovalDecision.Always,
			// Anything unrecognised (including empty string) folds into Deny —
			// matches the legacy WS frame's permissive parsing so the agent loop
			// can't get stuck waiting on a malformed client payload.
			_        => ApprovalDecision.Deny,
		};

		bool resolved = await approvals.ResolveAsync(approvalId, decision, "web");
		if (!resolved) throw new KeyNotFoundException($"No pending approval with ID '{approvalId}'");
		return this.NoContent();
	}
}
