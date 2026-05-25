using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.MultiRun;

public sealed record MultiRunTargetDto(
	[property: JsonPropertyName("chat_id")] [Required, MaxLength(64)] string  ChatId,
	[property: JsonPropertyName("agent_name")] [MaxLength(64)]        string? AgentName,
	[property: JsonPropertyName("model")] [MaxLength(128)]            string? Model
);

public sealed record MultiRunRequest(
	[property: JsonPropertyName("prompt")] [Required]              string                       Prompt,
	[property: JsonPropertyName("targets")] [Required, MinLength(1)] IReadOnlyList<MultiRunTargetDto> Targets
);

public sealed record MultiRunResponse(
	[property: JsonPropertyName("run_id")] string RunId
);
