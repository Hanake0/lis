namespace Lis.Core.Util;

public enum SummarizationPolicy { Prune, Summarize }

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolSummarizationAttribute(SummarizationPolicy policy) : Attribute {
	public SummarizationPolicy Policy { get; } = policy;
}
