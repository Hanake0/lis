using System.Text.Json;

using Lis.Api.Schemas.Agents;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lis.Api.Controllers;

/// <summary>
/// Full agent CRUD. The AgentEntity now carries the OC-shaped settings (mode,
/// temperature, top_p, prompt, permission_json, scope, disabled) so the byte-
/// identical AgentsPage / AgentsSidebar can drive the create/update/delete
/// flows directly. Built-in agents (<c>is_default = true</c>) can be updated
/// but not deleted — same constraint OC enforces on its `native` agents.
/// </summary>
[ApiController]
[Route("api/agents")]
[Tags("Agents")]
[Produces("application/json")]
public sealed class AgentsController(LisDbContext db, TimeProvider time) :ControllerBase {

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
	[ProducesResponseType(typeof(AgentDto),       StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<ActionResult<AgentDto>> Get([FromRoute] string name, CancellationToken ct) {
		AgentEntity agent = await this.FindByNameAsync(name, ct);
		return this.Ok(AgentDto.From(agent));
	}

	[HttpPost]
	[ProducesResponseType(typeof(AgentDto),       StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<ActionResult<AgentDto>> Create([FromBody] CreateAgentRequest request, CancellationToken ct) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));
		string name = request.Name.Trim();
		if (string.IsNullOrEmpty(name)) throw new ArgumentException("name is required", nameof(request));

		bool exists = await db.Agents.AnyAsync(a => a.Name == name, ct);
		if (exists) {
			return this.Conflict(new ProblemDetails {
				Title  = "agent already exists",
				Detail = $"agent '{name}' already exists",
				Status = StatusCodes.Status409Conflict,
			});
		}

		// Default the model from the system default agent so a new custom agent
		// is immediately usable. Falls back to "anthropic/claude-sonnet-4" when
		// no default exists (very fresh installs).
		string? defaultModel = request.Model?.Trim();
		if (string.IsNullOrEmpty(defaultModel)) {
			defaultModel = await db.Agents
				.Where(a => a.IsDefault)
				.Select(a => a.Model)
				.FirstOrDefaultAsync(ct);
		}
		if (string.IsNullOrEmpty(defaultModel)) defaultModel = "anthropic/claude-sonnet-4";

		string? validatedPermission = ValidatePermissionJson(request.PermissionJson);
		DateTimeOffset now = time.GetUtcNow();

		AgentEntity agent = new() {
			Name             = name,
			DisplayName      = TrimToNull(request.DisplayName),
			Description      = TrimToNull(request.Description),
			Model            = defaultModel,
			ThinkingEffort   = TrimToNull(request.ThinkingEffort),
			ToolProfile      = TrimToNull(request.ToolProfile),
			MaxTokens        = request.MaxTokens     ?? 4096,
			ContextBudget    = request.ContextBudget ?? 12000,
			Mode             = TrimToNull(request.Mode),
			Prompt           = TrimToNull(request.Prompt),
			Temperature      = request.Temperature,
			TopP             = request.TopP,
			PermissionJson   = validatedPermission,
			Scope            = TrimToNull(request.Scope),
			Disabled         = request.Disabled ?? false,
			IsDefault        = false,
			CreatedAt        = now,
			UpdatedAt        = now,
		};
		db.Agents.Add(agent);
		await db.SaveChangesAsync(ct);

		return this.CreatedAtAction(nameof(this.Get), new { name = agent.Name }, AgentDto.From(agent));
	}

	[HttpPatch("{name}")]
	[ProducesResponseType(typeof(AgentDto),       StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<AgentDto>> Update(
		[FromRoute] string              name,
		[FromBody]  UpdateAgentRequest  request,
		CancellationToken               ct
	) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));

		AgentEntity agent = await this.FindByNameAsync(name, ct);

		if (request.DisplayName    is not null) agent.DisplayName    = TrimToNull(request.DisplayName);
		if (request.Description    is not null) agent.Description    = TrimToNull(request.Description);
		if (request.Model          is not null) agent.Model          = request.Model.Trim();
		if (request.ThinkingEffort is not null) agent.ThinkingEffort = TrimToNull(request.ThinkingEffort);
		if (request.ToolProfile    is not null) agent.ToolProfile    = TrimToNull(request.ToolProfile);
		if (request.MaxTokens      is not null) agent.MaxTokens      = request.MaxTokens.Value;
		if (request.ContextBudget  is not null) agent.ContextBudget  = request.ContextBudget.Value;
		if (request.Mode           is not null) agent.Mode           = TrimToNull(request.Mode);
		if (request.Prompt         is not null) agent.Prompt         = TrimToNull(request.Prompt);
		if (request.Temperature    is not null) agent.Temperature    = request.Temperature;
		if (request.TopP           is not null) agent.TopP           = request.TopP;
		if (request.PermissionJson is not null) agent.PermissionJson = ValidatePermissionJson(request.PermissionJson);
		if (request.Scope          is not null) agent.Scope          = TrimToNull(request.Scope);
		if (request.Disabled       is not null) agent.Disabled       = request.Disabled.Value;

		agent.UpdatedAt = time.GetUtcNow();
		await db.SaveChangesAsync(ct);

		return this.Ok(AgentDto.From(agent));
	}

	[HttpDelete("{name}")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<IActionResult> Delete([FromRoute] string name, CancellationToken ct) {
		AgentEntity agent = await this.FindByNameAsync(name, ct);

		if (agent.IsDefault) {
			return this.Conflict(new ProblemDetails {
				Title  = "cannot delete built-in agent",
				Detail = $"agent '{name}' is the system default; clear is_default before deleting",
				Status = StatusCodes.Status409Conflict,
			});
		}

		db.Agents.Remove(agent);
		await db.SaveChangesAsync(ct);
		return this.NoContent();
	}

	// ── helpers ────────────────────────────────────────────────────

	private async Task<AgentEntity> FindByNameAsync(string name, CancellationToken ct) {
		AgentEntity? agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == name, ct);
		return agent ?? throw new KeyNotFoundException($"Agent '{name}' not found");
	}

	private static string? TrimToNull(string? value) {
		if (value is null) return null;
		string trimmed = value.Trim();
		return trimmed.Length == 0 ? null : trimmed;
	}

	/// <summary>
	/// Validate that a permission tree is valid JSON if provided. Returns the canonical
	/// JSON form (no extraneous whitespace) so all stored rows look identical when read.
	/// </summary>
	private static string? ValidatePermissionJson(string? value) {
		if (string.IsNullOrWhiteSpace(value)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(value);
			return doc.RootElement.GetRawText();
		} catch (JsonException ex) {
			throw new ArgumentException($"permission_json must be valid JSON: {ex.Message}", nameof(value));
		}
	}
}
