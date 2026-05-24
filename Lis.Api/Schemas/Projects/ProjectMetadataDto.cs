using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Projects;

/// <summary>Single key/value entry returned from the per-project metadata KV store.</summary>
public sealed record ProjectMetadataEntryDto(
	[property: JsonPropertyName("key")]        string Key,
	[property: JsonPropertyName("value")]      string Value,
	[property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt
);

/// <summary>
/// Response shape for the "single key fetch" endpoint. Splits hit/miss so the client
/// can distinguish "empty string was saved" from "no row exists".
/// </summary>
public sealed record ProjectMetadataValueDto(
	[property: JsonPropertyName("key")]        string Key,
	[property: JsonPropertyName("value")]      string? Value,
	[property: JsonPropertyName("found")]      bool Found,
	[property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt
);

/// <summary>Body for `PUT /api/projects/{id}/metadata?key=`. Value is required (use DELETE to clear).</summary>
public sealed record SetProjectMetadataRequest(
	[property: JsonPropertyName("value"), Required] string Value
);
