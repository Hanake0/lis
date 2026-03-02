namespace Lis.Core.Configuration;

public sealed class ModelSettings {
	public string Model         { get; init; } = "";
	public int    MaxTokens     { get; init; } = 4096;
	public int    ContextBudget { get; init; } = 12000;
}
