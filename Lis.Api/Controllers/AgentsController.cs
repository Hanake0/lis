using Lis.Api.Schemas.Agents;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// Read-only agent surface. Stage 13 will replace the WebChannel <c>list_agents</c>
/// / <c>get_agent</c> / <c>set_agent</c> handlers wholesale; this controller covers
/// the queries the sidebar + agent picker need today.
///
/// CRUD is intentionally out of scope here: AgentEntity has Lis-specific knobs
/// (thinking_effort, tool_profile, context_budget) that don't 1:1 map to OC's
/// AgentConfig (temperature, top_p, mode, permission). The webview's
/// useAgentsStore exposes the CRUD methods as throw-stubs so the AgentsPage
/// compiles, and we wire them properly in Stage 13.
/// </summary>
[ApiController]
[Route("api/agents")]
[Tags("Agents")]
[Produces("application/json")]
public sealed class AgentsController(LisDbContext db) :ControllerBase {

	[HttpGet]
	[ProducesResponseType(typeof(AgentDto[]), StatusCodes.Status200OK)]
	public async Task<ActionResult<AgentDto[]>> List(CancellationToken ct) {
		AgentDto[] agents = await db.Agents
			.OrderBy(a => a.IsDefault ? 0 : 1)  // built-ins first, custom after
			.ThenBy(a => a.Name)
			.Select(a => AgentDto.From(a))
			.ToArrayAsync(ct);
		return this.Ok(agents);
	}

	[HttpGet("{name}")]
	[ProducesResponseType(typeof(AgentDto),         StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status404NotFound)]
	public async Task<ActionResult<AgentDto>> Get([FromRoute] string name, CancellationToken ct) {
		AgentEntity? agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == name, ct);
		if (agent is null) throw new KeyNotFoundException($"Agent '{name}' not found");
		return this.Ok(AgentDto.From(agent));
	}
}
