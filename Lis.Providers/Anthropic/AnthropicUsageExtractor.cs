using Lis.Core.Channel;

using Microsoft.Extensions.AI;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Extracts <see cref="TokenUsage"/> from Anthropic SDK streaming metadata.
/// Cache token keys match <c>Anthropic.SDK.Messaging.ChatClientHelper.CreateUsageDetails</c>
/// (which uses <c>nameof()</c>). Reasoning tokens come from
/// <c>UsageDetails.ReasoningTokenCount</c> when present; Anthropic typically folds them
/// into <c>output_tokens</c>, so this is often null/zero — the output count already includes them.
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

		int thinkingTokens = (int)(details.ReasoningTokenCount ?? 0);

		return new TokenUsage(inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, thinkingTokens);
	}
}
