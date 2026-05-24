using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Agents;

/// <summary>
/// Read-side agent DTO. The OC <c>AgentWithExtras</c> the webview consumes has a
/// scope (user/project) + group (file-system subfolder) — neither concept exists
/// in Lis where every agent is a DB row, so we expose <c>native</c> (mapped from
/// <c>is_default</c>) and let the consumer treat <c>scope=user</c>/<c>group=undefined</c>
/// as the default.
/// </summary>
public sealed record AgentDto(
	[property: JsonPropertyName("name")]                  string  Name,
	[property: JsonPropertyName("display_name")]          string? DisplayName,
	[property: JsonPropertyName("model")]                 string  Model,
	[property: JsonPropertyName("thinking_effort")]       string? ThinkingEffort,
	[property: JsonPropertyName("tool_profile")]          string? ToolProfile,
	[property: JsonPropertyName("is_default")]            bool    IsDefault,
	[property: JsonPropertyName("native")]                bool    Native,
	[property: JsonPropertyName("max_tokens")]            int     MaxTokens,
	[property: JsonPropertyName("context_budget")]        int     ContextBudget,
	[property: JsonPropertyName("description")]           string? Description
) {
	public static AgentDto From(AgentEntity a) => new(
		Name:           a.Name,
		DisplayName:    a.DisplayName,
		Model:          a.Model,
		ThinkingEffort: a.ThinkingEffort,
		ToolProfile:    a.ToolProfile,
		IsDefault:      a.IsDefault,
		// Lis treats "is_default = true" as the built-in flag — OC's `isAgentBuiltIn` checks
		// `native === true || builtIn === true`, so we expose `native` here.
		Native:         a.IsDefault,
		MaxTokens:      a.MaxTokens,
		ContextBudget:  a.ContextBudget,
		Description:    null
	);
}
