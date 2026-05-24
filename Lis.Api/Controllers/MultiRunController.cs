using Lis.Api.Schemas.MultiRun;
using Lis.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Fan-out endpoint: takes one prompt and one list of chat targets, returns a run
/// id immediately, and pushes <c>multi_run_progress</c> SSE events for each target
/// as it completes. Stateless — runs aren't persisted, so a client that misses
/// progress events while disconnected won't see them on reconnect.
/// </summary>
[ApiController]
[Route("api/multi-run")]
[Tags("MultiRun")]
[Produces("application/json")]
public sealed class MultiRunController(IMultiRunCoordinator coordinator) :ControllerBase {

	[HttpPost]
	[ProducesResponseType(typeof(MultiRunResponse), StatusCodes.Status202Accepted)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public ActionResult<MultiRunResponse> Run([FromBody] MultiRunRequest request) {
		string runId = coordinator.Start(
			request.Prompt,
			request.Targets.Select(t => new MultiRunTarget(t.ChatId, t.AgentName, t.Model)).ToList()
		);
		return this.Accepted(string.Empty, new MultiRunResponse(runId));
	}
}
