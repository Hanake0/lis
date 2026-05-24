using Lis.Api.Schemas.Git;
using Lis.Git.Services;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// User-managed git identity profiles plus a read-only window onto the host's
/// global git config and a default-identity pointer. Used by the OC-ported
/// settings pages and the directory-add dialog to label/pick the identity that
/// should be active in a given working tree.
/// </summary>
[ApiController]
[Route("api/git/identities")]
[Tags("GitIdentities")]
[Produces("application/json")]
public sealed class GitIdentitiesController(
	IGitIdentityService identities,
	IGitService         git
) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(GitIdentityDto[]), StatusCodes.Status200OK)]
	public async Task<ActionResult<GitIdentityDto[]>> List(CancellationToken ct) {
		IReadOnlyList<GitIdentityEntity> all = await identities.ListAsync(ct);
		return this.Ok(all.Select(GitIdentityDto.From).ToArray());
	}

	[HttpGet("{externalId}")]
	[ProducesResponseType(typeof(GitIdentityDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<GitIdentityDto>> Get([FromRoute] string externalId, CancellationToken ct) {
		GitIdentityEntity? row = await identities.GetByExternalIdAsync(externalId, ct);
		return row is null
			? this.NotFound($"Identity '{externalId}' not found")
			: this.Ok(GitIdentityDto.From(row));
	}

	[HttpPost]
	[ProducesResponseType(typeof(GitIdentityDto), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<GitIdentityDto>> Create([FromBody] CreateGitIdentityRequest request, CancellationToken ct) {
		GitIdentityEntity created = await identities.CreateAsync(new GitIdentityEntity {
			ExternalId = request.Id ?? string.Empty,
			Name       = request.Name,
			UserName   = request.UserName,
			UserEmail  = request.UserEmail,
			AuthType   = request.AuthType,
			SshKeyPath = request.SshKeyPath,
			Host       = request.Host,
			Color      = request.Color,
			Icon       = request.Icon,
		}, ct);

		GitIdentityDto dto = GitIdentityDto.From(created);
		return this.CreatedAtAction(nameof(this.Get), new { externalId = dto.Id }, dto);
	}

	[HttpPatch("{externalId}")]
	[ProducesResponseType(typeof(GitIdentityDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<GitIdentityDto>> Update(
		[FromRoute] string                    externalId,
		[FromBody]  UpdateGitIdentityRequest  request,
		CancellationToken                     ct
	) {
		GitIdentityEntity updated = await identities.UpdateAsync(externalId, e => {
			if (request.Name       is not null) e.Name       = request.Name;
			if (request.UserName   is not null) e.UserName   = request.UserName;
			if (request.UserEmail  is not null) e.UserEmail  = request.UserEmail;
			if (request.AuthType   is not null) e.AuthType   = request.AuthType;
			if (request.SshKeyPath is not null) e.SshKeyPath = request.SshKeyPath;
			if (request.Host       is not null) e.Host       = request.Host;
			if (request.Color      is not null) e.Color      = request.Color;
			if (request.Icon       is not null) e.Icon       = request.Icon;
		}, ct);
		return this.Ok(GitIdentityDto.From(updated));
	}

	[HttpDelete("{externalId}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Delete([FromRoute] string externalId, CancellationToken ct) {
		await identities.DeleteAsync(externalId, ct);
		return this.NoContent();
	}

	[HttpGet("global")]
	[ProducesResponseType(typeof(GlobalGitIdentityDto), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<ActionResult<GlobalGitIdentityDto>> Global(CancellationToken ct) {
		GlobalGitIdentity? id = await git.GetGlobalIdentityAsync(ct);
		return id is null ? this.NoContent() : this.Ok(GlobalGitIdentityDto.From(id));
	}

	[HttpGet("default")]
	[ProducesResponseType(typeof(DefaultGitIdentityDto), StatusCodes.Status200OK)]
	public async Task<ActionResult<DefaultGitIdentityDto>> GetDefault(CancellationToken ct) {
		string? id = await identities.GetDefaultIdentityExternalIdAsync(ct);
		return this.Ok(new DefaultGitIdentityDto(id));
	}

	[HttpPut("default")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> SetDefault([FromBody] SetDefaultGitIdentityRequest request, CancellationToken ct) {
		await identities.SetDefaultIdentityExternalIdAsync(request.ExternalId, ct);
		return this.NoContent();
	}
}
