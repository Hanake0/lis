using Lis.Api.Schemas.GitHub;
using Lis.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// GitHub identity (PAT in / out / status) and PR status lookup. Token is never
/// returned in any response — only the login + scopes.
/// </summary>
[ApiController]
[Route("api/github")]
[Tags("GitHub")]
[Produces("application/json")]
public sealed class GitHubController(IGitHubService github) :ControllerBase {

	[HttpGet("status")]
	[ProducesResponseType(typeof(GitHubStatusDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<GitHubStatusDto>> Status(CancellationToken ct) {
		GitHubStatus status = await github.GetStatusAsync(ct);
		return this.Ok(GitHubStatusDto.From(status));
	}

	[HttpPut("token")]
	[ProducesResponseType(typeof(GitHubStatusDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<GitHubStatusDto>> SetToken([FromBody] SetGitHubTokenRequest request, CancellationToken ct) {
		GitHubStatus status = await github.SetTokenAsync(request.Token, ct);
		return this.Ok(GitHubStatusDto.From(status));
	}

	[HttpDelete("token")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> ClearToken(CancellationToken ct) {
		await github.ClearTokenAsync(ct);
		return this.NoContent();
	}

	[HttpPost("pr-status")]
	[ProducesResponseType(typeof(PrStatusResponse), StatusCodes.Status200OK)]
	public async Task<ActionResult<PrStatusResponse>> PrStatus([FromBody] PrStatusRequest request, CancellationToken ct) {
		IReadOnlyDictionary<string, PrStatus> results = await github.GetPrStatusAsync(
			request.Keys.Select(k => new PrKey(k.Owner, k.Repo, k.Branch)).ToArray(),
			ct
		);
		Dictionary<string, PrStatusDto> dtos = results.ToDictionary(kv => kv.Key, kv => PrStatusDto.From(kv.Value));
		return this.Ok(new PrStatusResponse(dtos));
	}
}
