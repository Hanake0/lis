using System.Text.Json.Serialization;

using Lis.Core.Configuration;

namespace Lis.Api.Schemas.Providers;

/// <summary>
/// Response payload for <c>GET /api/providers</c>. Mirrors the OC
/// <c>Provider</c> SDK shape so the byte-identical webview ModelControls
/// (model picker, effort dropdown, provider icons) reads it without
/// per-render adaptation.
/// </summary>
public sealed record ProvidersResponse(
	[property: JsonPropertyName("providers")] ProviderDto[] Providers,
	[property: JsonPropertyName("default")]   ProviderDefaults Default
);

public sealed record ProviderDto(
	[property: JsonPropertyName("id")]     string                            Id,
	[property: JsonPropertyName("name")]   string                            Name,
	[property: JsonPropertyName("models")] Dictionary<string, ModelMetaDto>  Models
) {
	public static ProviderDto From(IProviderMetadata p) => new(
		Id:     p.Id,
		Name:   p.Name,
		Models: p.Models.ToDictionary(kv => kv.Key, kv => ModelMetaDto.From(kv.Value))
	);
}

public sealed record ModelMetaDto(
	[property: JsonPropertyName("id")]            string             Id,
	[property: JsonPropertyName("name")]          string             Name,
	[property: JsonPropertyName("release_date")]  string?            ReleaseDate,
	[property: JsonPropertyName("attachment")]    ModelAttachmentDto Attachment,
	[property: JsonPropertyName("reasoning")]     ModelReasoningDto  Reasoning,
	[property: JsonPropertyName("cost")]          ModelCostDto       Cost,
	[property: JsonPropertyName("limit")]         ModelLimitDto      Limit,
	[property: JsonPropertyName("experimental")]  bool               Experimental
) {
	public static ModelMetaDto From(ModelMetadata m) => new(
		Id:           m.Id,
		Name:         m.Name,
		ReleaseDate:  m.ReleaseDate,
		Attachment:   new ModelAttachmentDto(m.Attachment.Image, m.Attachment.Video, m.Attachment.Audio),
		Reasoning:    new ModelReasoningDto(m.Reasoning.Enabled, [..m.Reasoning.SupportedEfforts]),
		Cost:         new ModelCostDto(m.Cost.Input, m.Cost.Output, m.Cost.CacheRead, m.Cost.CacheWrite),
		Limit:        new ModelLimitDto(m.Limit.Context, m.Limit.Output),
		Experimental: m.Experimental
	);
}

public sealed record ModelAttachmentDto(
	[property: JsonPropertyName("image")] bool Image,
	[property: JsonPropertyName("video")] bool Video,
	[property: JsonPropertyName("audio")] bool Audio
);

public sealed record ModelReasoningDto(
	[property: JsonPropertyName("enabled")]            bool     Enabled,
	[property: JsonPropertyName("supported_efforts")]  string[] SupportedEfforts
);

public sealed record ModelCostDto(
	[property: JsonPropertyName("input")]       decimal Input,
	[property: JsonPropertyName("output")]      decimal Output,
	[property: JsonPropertyName("cache_read")]  decimal CacheRead,
	[property: JsonPropertyName("cache_write")] decimal CacheWrite
);

public sealed record ModelLimitDto(
	[property: JsonPropertyName("context")] int Context,
	[property: JsonPropertyName("output")]  int Output
);

public sealed record ProviderDefaults(
	[property: JsonPropertyName("model")] string? Model,
	[property: JsonPropertyName("agent")] string? Agent
);
