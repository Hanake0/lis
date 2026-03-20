namespace Lis.Agent.Commands;

public sealed class ApproveCommand(IApprovalService approvalService) : IChatCommand {
	public string[] Triggers => ["/approve"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Args is null or { Length: 0 })
			return "Usage: /approve <id> [once|always|deny]";

		string[] parts    = ctx.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string   id       = parts[0];
		string   decision = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "once";

		ApprovalDecision parsedDecision = decision switch {
			"once"   => ApprovalDecision.Once,
			"always" => ApprovalDecision.Always,
			"deny"   => ApprovalDecision.Deny,
			_        => ApprovalDecision.Once
		};

		bool resolved = await approvalService.ResolveAsync(id, parsedDecision, ctx.Message.SenderId);

		if (!resolved) return $"No pending approval with ID '{id}'.";

		return parsedDecision switch {
			ApprovalDecision.Once   => $"✅ Approved (once): {id}",
			ApprovalDecision.Always => $"✅ Approved (always): {id}",
			ApprovalDecision.Deny   => $"❌ Denied: {id}",
			_                       => $"✅ Approved: {id}"
		};
	}
}

public sealed class DenyCommand(IApprovalService approvalService) : IChatCommand {
	public string[] Triggers => ["/deny"];

	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Args is null or { Length: 0 })
			return "Usage: /deny <id>";

		string id       = ctx.Args.Trim().Split(' ')[0];
		bool   resolved = await approvalService.ResolveAsync(id, ApprovalDecision.Deny, ctx.Message.SenderId);

		if (!resolved) return $"No pending approval with ID '{id}'.";

		return $"❌ Denied: {id}";
	}
}
