using System.ComponentModel;

using Lis.Core.Util;
using Lis.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class ResponsePlugin(IServiceScopeFactory scopeFactory) {

	[KernelFunction("react_to_message")]
	[Description("React to a message with an emoji. Optionally target a specific message by its ID (the number in brackets before the message).")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	public async Task<string> ReactToMessageAsync(
		[Description("Emoji to react with (e.g. '👍', '❤️', '😂')")]
		string emoji,
		[Description("Optional message ID to react to (e.g. 42). Defaults to the latest message.")]
		long? messageId = null) {

		if (ToolContext.Channel is null || ToolContext.ChatId is null)
			return "No active channel.";

		string? externalId;

		if (messageId is not null) {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			externalId = await db.Messages
				.Where(m => m.Id == messageId)
				.Select(m => m.ExternalId)
				.FirstOrDefaultAsync();

			if (externalId is null)
				return $"Message {messageId} not found.";
		} else {
			externalId = ToolContext.MessageExternalId;
			if (externalId is null)
				return "No message to react to.";
		}

		await ToolContext.Channel.ReactAsync(externalId, ToolContext.ChatId, emoji);
		return "ok";
	}
}
