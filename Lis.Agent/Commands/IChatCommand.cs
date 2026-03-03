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
	LisDbContext    Db,
	string?         Args = null);

public sealed record CommandMatch(IChatCommand Command, string? Args);

public sealed class CommandRouter(IEnumerable<IChatCommand> commands) {
	public CommandMatch? Match(string? messageBody) {
		if (string.IsNullOrWhiteSpace(messageBody)) return null;

		string trimmed = messageBody.Trim();
		foreach (IChatCommand command in commands)
			foreach (string t in command.Triggers)
				if (trimmed.Equals(t, StringComparison.OrdinalIgnoreCase))
					return new(command, null);
				else if (trimmed.StartsWith(t + " ", StringComparison.OrdinalIgnoreCase))
					return new(command, trimmed[(t.Length + 1)..].TrimStart());

		return null;
	}
}
