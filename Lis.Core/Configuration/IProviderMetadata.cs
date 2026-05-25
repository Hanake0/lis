namespace Lis.Core.Configuration;

/// <summary>
/// Static description of an AI provider and the models it serves.
///
/// Registered by each provider's <c>Add*</c> extension method (e.g.
/// <c>AddAnthropic</c>) when the provider is enabled. The
/// <c>ProvidersController</c> enumerates every <see cref="IProviderMetadata"/>
/// in DI to populate <c>GET /api/providers</c>, which the webview's
/// <c>useConfigStore</c> consumes to render the model picker, effort
/// dropdown, thinking-mode toggle, and provider icons.
///
/// Lis ships static metadata — model availability follows the deployed
/// provider library version, not a server query. If you add a new model on
/// the upstream provider, update the corresponding <see cref="Models"/>
/// dictionary in this assembly. There is no live model-list discovery.
/// </summary>
public interface IProviderMetadata {
	string Id   { get; }
	string Name { get; }
	IReadOnlyDictionary<string, ModelMetadata> Models { get; }
}

/// <summary>Capability + cost description of one model under a provider.</summary>
public sealed record ModelMetadata(
	string             Id,
	string             Name,
	string?            ReleaseDate,
	ModelAttachment    Attachment,
	ModelReasoning     Reasoning,
	ModelCost          Cost,
	ModelLimit         Limit,
	bool               Experimental
);

/// <summary>Per-modality input the model accepts.</summary>
public sealed record ModelAttachment(bool Image, bool Video, bool Audio);

/// <summary>
/// Reasoning capability. <see cref="SupportedEfforts"/> drives the effort
/// dropdown (low/medium/high/maximum, or whatever the provider exposes).
/// Empty array means no effort selector for this model.
/// </summary>
public sealed record ModelReasoning(bool Enabled, IReadOnlyList<string> SupportedEfforts);

/// <summary>Per-million-token cost in USD across the four pricing tiers.</summary>
public sealed record ModelCost(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite);

/// <summary>Hard token limits the model enforces.</summary>
public sealed record ModelLimit(int Context, int Output);
