namespace Lis.Providers.Anthropic;

public sealed class AnthropicOptions {
	public required string ApiKey { get; init; }
	public string Model { get; init; } = "claude-sonnet-4-20250514";
	public int MaxTokens { get; init; } = 4096;
	public int ContextBudget { get; init; } = 12000;
}
