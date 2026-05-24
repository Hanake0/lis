using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Lis.Persistence.Entities;

namespace Lis.Api.Schemas.Scheduled;

public sealed record ScheduledTaskDto(
	[property: JsonPropertyName("id")]             string         Id,
	[property: JsonPropertyName("project_id")]     string?        ProjectId,
	[property: JsonPropertyName("name")]           string         Name,
	[property: JsonPropertyName("cron_expr")]      string         CronExpression,
	[property: JsonPropertyName("prompt")]         string         Prompt,
	[property: JsonPropertyName("agent_name")]     string?        AgentName,
	[property: JsonPropertyName("model")]          string?        Model,
	[property: JsonPropertyName("timezone")]       string?        Timezone,
	[property: JsonPropertyName("enabled")]        bool           Enabled,
	[property: JsonPropertyName("next_run_at")]    DateTimeOffset? NextRunAt,
	[property: JsonPropertyName("last_run_at")]    DateTimeOffset? LastRunAt,
	[property: JsonPropertyName("last_status")]    string?        LastStatus,
	[property: JsonPropertyName("last_error")]     string?        LastError,
	[property: JsonPropertyName("created_at")]     DateTimeOffset CreatedAt,
	[property: JsonPropertyName("updated_at")]     DateTimeOffset UpdatedAt
) {
	public static ScheduledTaskDto From(ScheduledTaskEntity t, string? projectExternalId) => new(
		Id:             t.ExternalId,
		ProjectId:      projectExternalId,
		Name:           t.Name,
		CronExpression: t.CronExpression,
		Prompt:         t.Prompt,
		AgentName:      t.AgentName,
		Model:          t.Model,
		Timezone:       t.Timezone,
		Enabled:        t.Enabled,
		NextRunAt:      t.NextRunAt,
		LastRunAt:      t.LastRunAt,
		LastStatus:     t.LastStatus,
		LastError:      t.LastError,
		CreatedAt:      t.CreatedAt,
		UpdatedAt:      t.UpdatedAt
	);
}

public sealed record CreateScheduledTaskRequest(
	[property: JsonPropertyName("name"),       Required, MaxLength(256)] string  Name,
	[property: JsonPropertyName("cron_expr"),  Required, MaxLength(128)] string  CronExpression,
	[property: JsonPropertyName("prompt"),     Required]                 string  Prompt,
	[property: JsonPropertyName("project_id"), MaxLength(64)]            string? ProjectId,
	[property: JsonPropertyName("agent_name"), MaxLength(64)]            string? AgentName,
	[property: JsonPropertyName("model"),      MaxLength(128)]           string? Model,
	[property: JsonPropertyName("timezone"),   MaxLength(64)]            string? Timezone,
	[property: JsonPropertyName("enabled")]                              bool    Enabled = true
);

public sealed record UpdateScheduledTaskRequest(
	[property: JsonPropertyName("name"),       MaxLength(256)] string? Name,
	[property: JsonPropertyName("cron_expr"),  MaxLength(128)] string? CronExpression,
	[property: JsonPropertyName("prompt")]                     string? Prompt,
	[property: JsonPropertyName("project_id"), MaxLength(64)]  string? ProjectId,
	[property: JsonPropertyName("agent_name"), MaxLength(64)]  string? AgentName,
	[property: JsonPropertyName("model"),      MaxLength(128)] string? Model,
	[property: JsonPropertyName("timezone"),   MaxLength(64)]  string? Timezone,
	[property: JsonPropertyName("enabled")]                    bool?   Enabled
);
