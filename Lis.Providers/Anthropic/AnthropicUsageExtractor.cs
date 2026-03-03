using Lis.Core.Channel;

using Microsoft.Extensions.AI;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Extracts <see cref="TokenUsage"/> from Anthropic SDK streaming metadata.
/// Uses exact key names from <c>Anthropic.SDK.Messaging.ChatClientHelper.CreateUsageDetails</c>
/// which maps <c>Usage.CacheReadInputTokens</c> → <c>"CacheReadInputTokens"</c> and
/// <c>Usage.CacheCreationInputTokens</c> → <c>"CacheCreationInputTokens"</c> via <c>nameof()</c>.
/// </summary>
public sealed class AnthropicUsageExtractor : IUsageExtractor {
	public TokenUsage? Extract(IReadOnlyDictionary<string, object?>? metadata) {
		if (metadata is null) return null;

		if (!metadata.TryGetValue("Usage", out object? usageObj) || usageObj is not UsageContent usageContent)
			return null;

		UsageDetails? details = usageContent.Details;
		if (details is null) return null;

		int inputTokens  = (int)(details.InputTokenCount  ?? 0);
		int outputTokens = (int)(details.OutputTokenCount ?? 0);

		if (inputTokens == 0 && outputTokens == 0) return null;

		int cacheReadTokens     = 0;
		int cacheCreationTokens = 0;
		if (details.AdditionalCounts is not null) {
			if (details.AdditionalCounts.TryGetValue("CacheReadInputTokens", out long cacheRead))
				cacheReadTokens = (int)cacheRead;
			if (details.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out long cacheCreation))
				cacheCreationTokens = (int)cacheCreation;
		}

		// Anthropic SDK 5.10 does not expose thinking tokens separately —
		// they are included in OutputTokenCount.
		return new TokenUsage(inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, ThinkingTokens: 0);
	}
}
