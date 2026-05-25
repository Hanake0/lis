using Lis.Api.Schemas.Providers;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// Enumerates DI-registered <see cref="IProviderMetadata"/> services to feed
/// the webview's model picker / effort dropdown / provider icons via
/// <c>useConfigStore.loadProviders</c>. <c>default.agent</c> / <c>default.model</c>
/// come from the agent row marked <c>is_default = true</c> — same source the
/// existing chat send path falls back to when no overrides are set.
/// </summary>
[ApiController]
[Route("api/providers")]
[Tags("Providers")]
[Produces("application/json")]
public sealed class ProvidersController(
	IEnumerable<IProviderMetadata> providers,
	LisDbContext                   db
) :ControllerBase {

	[HttpGet]
	[Trace("ProvidersController > List")]
	[ProducesResponseType(typeof(ProvidersResponse), StatusCodes.Status200OK)]
	public async Task<ActionResult<ProvidersResponse>> List(CancellationToken ct) {
		ProviderDto[] mapped = providers.Select(ProviderDto.From).ToArray();

		// Default agent + its model — the chat send path uses the same fallback
		// when no explicit overrides arrive. Returns nulls if no default agent
		// exists (fresh install with no agents registered yet).
		var def = await db.Agents
			.Where(a => a.IsDefault)
			.Select(a => new { a.Name, a.Model })
			.FirstOrDefaultAsync(ct);

		return this.Ok(new ProvidersResponse(
			Providers: mapped,
			Default:   new ProviderDefaults(Model: def?.Model, Agent: def?.Name)
		));
	}
}
