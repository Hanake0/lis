using Anthropic.SDK.Messaging;

using Lis.Core.Channel;

namespace Lis.Providers.Anthropic;

/// <summary>
/// Reads thinking deltas from the Anthropic SDK's per-event <see cref="MessageResponse"/>.
/// Streaming events surface as content_block_delta with <c>Delta.Type == "thinking_delta"</c>
/// and the text on <c>Delta.Thinking</c>.
/// </summary>
public sealed class AnthropicReasoningExtractor : IReasoningExtractor {
	public string? ExtractReasoningDelta(object? innerContent) {
		if (innerContent is not MessageResponse mr) return null;
		if (mr.Delta is null) return null;
		if (!string.Equals(mr.Delta.Type, "thinking_delta", StringComparison.Ordinal)) return null;
		return mr.Delta.Thinking;
	}
}
