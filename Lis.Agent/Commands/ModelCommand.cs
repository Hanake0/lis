using Lis.Core.Util;

using Microsoft.Extensions.DependencyInjection;

namespace Lis.Agent.Commands;

public sealed class ModelCommand(IServiceScopeFactory scopeFactory) : IChatCommand {
	// Kept for DI consistency; may be used for cross-agent model queries later.
	private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

	public string[] Triggers => ["/model"];
	public bool OwnerOnly => true;

	[Trace("ModelCommand > ExecuteAsync")]
	public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
		if (ctx.Args is null or { Length: 0 })
			return $"🧠 Current model: {ctx.Agent.Model}";

		string model = ctx.Args.Trim();
		ctx.Agent.Model     = model;
		ctx.Agent.UpdatedAt = DateTimeOffset.UtcNow;
		await ctx.Db.SaveChangesAsync(ct);

		return $"✅ Model updated to '{model}'.";
	}
}
