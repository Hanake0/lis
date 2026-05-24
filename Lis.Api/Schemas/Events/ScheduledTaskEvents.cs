using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Events;

/// <summary>Event names emitted on <c>/api/events</c> for scheduled-task lifecycle.</summary>
public static class ScheduledTaskEventNames {
	public const string Created = "scheduled_task_created";
	public const string Updated = "scheduled_task_updated";
	public const string Deleted = "scheduled_task_deleted";
	public const string Run     = "scheduled_task_run";
}

public sealed record ScheduledTaskCreatedPayload(
	[property: JsonPropertyName("task")] object Task
);

public sealed record ScheduledTaskUpdatedPayload(
	[property: JsonPropertyName("task")] object Task
);

public sealed record ScheduledTaskDeletedPayload(
	[property: JsonPropertyName("task_id")] string TaskId
);

public sealed record ScheduledTaskRunPayload(
	[property: JsonPropertyName("task_id")] string  TaskId,
	[property: JsonPropertyName("status")]  string  Status,
	[property: JsonPropertyName("error")]   string? Error
);
