namespace Lis.Core.Util;

public enum ToolAuthLevel { Open, OwnerOnly, ApprovalRequired }

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAuthorizationAttribute(ToolAuthLevel level) : Attribute {
	public ToolAuthLevel Level { get; } = level;
}
