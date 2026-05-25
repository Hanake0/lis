using Lis.Core.Configuration;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Static catalog of Anthropic models exposed to the webview via
/// <c>GET /api/providers</c>. Pricing/limits/capabilities lifted from
/// https://docs.anthropic.com/en/docs/models — keep in sync when a new
/// claude-* model lands or pricing changes.
///
/// Registered in DI by <c>AddAnthropic</c> only when
/// <c>ANTHROPIC_ENABLED=true</c>, so a deployment without Anthropic
/// configured returns an empty <c>providers</c> list and the picker
/// shows "no models" instead of misleading mock entries.
/// </summary>
public sealed class AnthropicProviderMetadata : IProviderMetadata {
	public string Id   => "anthropic";
	public string Name => "Anthropic";

	public IReadOnlyDictionary<string, ModelMetadata> Models { get; } = new Dictionary<string, ModelMetadata> {
		["claude-opus-4-7"] = new(
			Id:           "claude-opus-4-7",
			Name:         "Claude Opus 4.7",
			ReleaseDate:  "2026-02",
			Attachment:   new ModelAttachment(Image: true,  Video: false, Audio: false),
			Reasoning:    new ModelReasoning(Enabled: true, SupportedEfforts: ["low", "medium", "high", "maximum"]),
			Cost:         new ModelCost(Input: 15m,   Output: 75m,  CacheRead: 1.50m, CacheWrite: 18.75m),
			Limit:        new ModelLimit(Context: 200_000, Output: 32_000),
			Experimental: false
		),
		["claude-sonnet-4-6"] = new(
			Id:           "claude-sonnet-4-6",
			Name:         "Claude Sonnet 4.6",
			ReleaseDate:  "2025-12",
			Attachment:   new ModelAttachment(Image: true,  Video: false, Audio: false),
			Reasoning:    new ModelReasoning(Enabled: true, SupportedEfforts: ["low", "medium", "high"]),
			Cost:         new ModelCost(Input: 3m,    Output: 15m,  CacheRead: 0.30m, CacheWrite: 3.75m),
			Limit:        new ModelLimit(Context: 200_000, Output: 8_192),
			Experimental: false
		),
		["claude-haiku-4-5-20251001"] = new(
			Id:           "claude-haiku-4-5-20251001",
			Name:         "Claude Haiku 4.5",
			ReleaseDate:  "2025-10",
			Attachment:   new ModelAttachment(Image: true,  Video: false, Audio: false),
			Reasoning:    new ModelReasoning(Enabled: false, SupportedEfforts: []),
			Cost:         new ModelCost(Input: 0.80m, Output: 4m,   CacheRead: 0.08m, CacheWrite: 1m),
			Limit:        new ModelLimit(Context: 200_000, Output: 8_192),
			Experimental: false
		),
		// claude-sonnet-4-20250514 is the model id the live AnthropicProvider
		// targets by default (older sonnet 4). Kept here so the picker can
		// surface it for backward-compat installs.
		["claude-sonnet-4-20250514"] = new(
			Id:           "claude-sonnet-4-20250514",
			Name:         "Claude Sonnet 4",
			ReleaseDate:  "2025-05",
			Attachment:   new ModelAttachment(Image: true,  Video: false, Audio: false),
			Reasoning:    new ModelReasoning(Enabled: true, SupportedEfforts: ["low", "medium", "high"]),
			Cost:         new ModelCost(Input: 3m,    Output: 15m,  CacheRead: 0.30m, CacheWrite: 3.75m),
			Limit:        new ModelLimit(Context: 200_000, Output: 8_192),
			Experimental: false
		),
	};
}
