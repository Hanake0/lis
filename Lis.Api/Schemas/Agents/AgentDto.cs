using System.ComponentModel.DataAnnotations;
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
	[property: JsonPropertyName("description")]           string? Description,
	[property: JsonPropertyName("model")]                 string  Model,
	[property: JsonPropertyName("thinking_effort")]       string? ThinkingEffort,
	[property: JsonPropertyName("tool_profile")]          string? ToolProfile,
	[property: JsonPropertyName("is_default")]            bool    IsDefault,
	[property: JsonPropertyName("native")]                bool    Native,
	[property: JsonPropertyName("max_tokens")]            int     MaxTokens,
	[property: JsonPropertyName("context_budget")]        int     ContextBudget,
	[property: JsonPropertyName("mode")]                  string? Mode,
	[property: JsonPropertyName("prompt")]                string? Prompt,
	[property: JsonPropertyName("temperature")]           double? Temperature,
	[property: JsonPropertyName("top_p")]                 double? TopP,
	[property: JsonPropertyName("permission_json")]       string? PermissionJson,
	[property: JsonPropertyName("scope")]                 string? Scope,
	[property: JsonPropertyName("disabled")]              bool    Disabled
) {
	public static AgentDto From(AgentEntity a) => new(
		Name:           a.Name,
		DisplayName:    a.DisplayName,
		Description:    a.Description,
		Model:          a.Model,
		ThinkingEffort: a.ThinkingEffort,
		ToolProfile:    a.ToolProfile,
		IsDefault:      a.IsDefault,
		// Lis treats "is_default = true" as the built-in flag — OC's `isAgentBuiltIn` checks
		// `native === true || builtIn === true`, so we expose `native` here.
		Native:         a.IsDefault,
		MaxTokens:      a.MaxTokens,
		ContextBudget:  a.ContextBudget,
		Mode:           a.Mode,
		Prompt:         a.Prompt,
		Temperature:    a.Temperature,
		TopP:           a.TopP,
		PermissionJson: a.PermissionJson,
		Scope:          a.Scope,
		Disabled:       a.Disabled
	);
}

/// <summary>
/// Create payload. <c>Name</c> is required and unique; everything else is optional
/// with sensible defaults filled in by the controller (model defaults to the system
/// default agent's model so new custom agents are immediately usable).
/// </summary>
public sealed record CreateAgentRequest(
	[property: JsonPropertyName("name")] [Required, MaxLength(64)]            string  Name,
	[property: JsonPropertyName("display_name")] [MaxLength(128)]             string? DisplayName,
	[property: JsonPropertyName("description")] [MaxLength(2048)]             string? Description,
	[property: JsonPropertyName("model")] [MaxLength(128)]                    string? Model,
	[property: JsonPropertyName("thinking_effort")] [MaxLength(16)]           string? ThinkingEffort,
	[property: JsonPropertyName("tool_profile")] [MaxLength(32)]              string? ToolProfile,
	[property: JsonPropertyName("max_tokens")]                               int?    MaxTokens,
	[property: JsonPropertyName("context_budget")]                           int?    ContextBudget,
	[property: JsonPropertyName("mode")] [MaxLength(16)]                      string? Mode,
	[property: JsonPropertyName("prompt")]                                   string? Prompt,
	[property: JsonPropertyName("temperature")]                              double? Temperature,
	[property: JsonPropertyName("top_p")]                                    double? TopP,
	[property: JsonPropertyName("permission_json")]                          string? PermissionJson,
	[property: JsonPropertyName("scope")] [MaxLength(16)]                     string? Scope,
	[property: JsonPropertyName("disabled")]                                 bool?   Disabled
);

/// <summary>
/// Patch payload. Every field is nullable in the wire sense: <c>null</c> means
/// "don't change". To explicitly clear a string, send <c>""</c> (controller
/// normalises empty to <c>null</c> in the entity for optional columns).
/// </summary>
public sealed record UpdateAgentRequest(
	[property: JsonPropertyName("display_name")] [MaxLength(128)]             string? DisplayName,
	[property: JsonPropertyName("description")] [MaxLength(2048)]             string? Description,
	[property: JsonPropertyName("model")] [MaxLength(128)]                    string? Model,
	[property: JsonPropertyName("thinking_effort")] [MaxLength(16)]           string? ThinkingEffort,
	[property: JsonPropertyName("tool_profile")] [MaxLength(32)]              string? ToolProfile,
	[property: JsonPropertyName("max_tokens")]                               int?    MaxTokens,
	[property: JsonPropertyName("context_budget")]                           int?    ContextBudget,
	[property: JsonPropertyName("mode")] [MaxLength(16)]                      string? Mode,
	[property: JsonPropertyName("prompt")]                                   string? Prompt,
	[property: JsonPropertyName("temperature")]                              double? Temperature,
	[property: JsonPropertyName("top_p")]                                    double? TopP,
	[property: JsonPropertyName("permission_json")]                          string? PermissionJson,
	[property: JsonPropertyName("scope")] [MaxLength(16)]                     string? Scope,
	[property: JsonPropertyName("disabled")]                                 bool?   Disabled
);
