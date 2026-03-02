namespace Lis.Core.Channel;

public sealed record TokenUsage(
	int InputTokens,
	int OutputTokens,
	int CacheReadTokens,
	int CacheCreationTokens,
	int ThinkingTokens) {

	public int TotalInputTokens => this.InputTokens + this.CacheReadTokens + this.CacheCreationTokens;
}
