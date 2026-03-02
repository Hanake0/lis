using Lis.Core.Channel;
using Lis.Persistence;
using Lis.Persistence.Entities;

namespace Lis.Agent.Commands;

public interface IChatCommand {
	string[] Triggers { get; }
	Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct);
}

public sealed record CommandContext(
	IncomingMessage Message,
	ChatEntity      Chat,
	SessionEntity?  Session,
	LisDbContext    Db);

public sealed class CommandRouter(IEnumerable<IChatCommand> commands) {
	public IChatCommand? Match(string? messageBody) {
		if (string.IsNullOrWhiteSpace(messageBody)) return null;

		string trimmed = messageBody.Trim();
		foreach (IChatCommand command in commands)
			if (command.Triggers.Any(t => trimmed.Equals(t, StringComparison.OrdinalIgnoreCase)))
				return command;

		return null;
	}
}
