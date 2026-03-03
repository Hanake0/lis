namespace Lis.Core.Channel;

public interface IUsageExtractor {
	TokenUsage? Extract(IReadOnlyDictionary<string, object?>? metadata);
}
