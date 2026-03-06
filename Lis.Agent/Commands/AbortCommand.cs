namespace Lis.Agent.Commands;

public sealed class AbortCommand : IChatCommand {
	public string[] Triggers => ["/abort", "/stop", "/cancel"];

	public Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct)
		=> Task.FromResult("⛔ Aborted.");
}
