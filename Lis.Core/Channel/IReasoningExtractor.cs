namespace Lis.Core.Channel;

/// <summary>
/// Provider-specific bridge that extracts reasoning (thinking) text from a streaming
/// chunk's raw inner content. The SK→M.E.AI adapter strips reasoning content from
/// Items, but the underlying provider object is still available on
/// <c>StreamingChatMessageContent.InnerContent</c>.
/// </summary>
public interface IReasoningExtractor {
	/// <summary>
	/// Returns the reasoning delta text if this chunk's <paramref name="innerContent"/>
	/// represents a thinking event from the provider; null otherwise.
	/// </summary>
	string? ExtractReasoningDelta(object? innerContent);
}
