using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lis.Agent;

/// <summary>
/// One-shot LLM invocation against a chat's agent/model. Unlike the regular conversation
/// flow, the prompts here are NOT saved to the chat history — these are "background"
/// generations used by the git view (commit message, PR description) where polluting the
/// chat with the prompt + structured-JSON response would be noisy.
///
/// The contract matches OC's `session.prompt` shape: a visible prompt the user sees in
/// their session, an optional hidden prompt with the structured-output instructions, and
/// a parsed assistant text body for the caller to extract JSON from.
/// </summary>
public interface IChatGenerationService {
	Task<ChatGenerationService.GenerationResult> GenerateAsync(
		ChatGenerationService.GenerationRequest request,
		CancellationToken                        ct
	);
}

public sealed class ChatGenerationService(
	IChatClient                       chatClient,
	AgentService                      agentService,
	IServiceScopeFactory              scopeFactory,
	ILogger<ChatGenerationService>    logger
) :IChatGenerationService {

	public sealed record GenerationRequest(
		string  ChatExternalId,
		string  VisiblePrompt,
		string? HiddenPrompt,
		string  Kind,            // "commit" | "pr" — currently informational, drives logging
		string? ModelOverride
	);

	public sealed record GenerationResult(string Text, int InputTokens, int OutputTokens);

	[Trace("ChatGenerationService > GenerateAsync")]
	public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(request.ChatExternalId))
			throw new ArgumentException("chatExternalId is required", nameof(request));
		if (string.IsNullOrWhiteSpace(request.VisiblePrompt))
			throw new ArgumentException("visiblePrompt is required", nameof(request));

		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ChatEntity chat = await db.Chats
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == request.ChatExternalId, ct)
			?? throw new KeyNotFoundException($"chat '{request.ChatExternalId}' not found");

		AgentEntity agent = await agentService.ResolveForChatAsync(db, chat, ct);
		string      model = !string.IsNullOrWhiteSpace(request.ModelOverride)
			? request.ModelOverride
			: agent.Model;
		if (string.IsNullOrWhiteSpace(model))
			throw new InvalidOperationException("no model configured for chat or agent");

		// Compose the prompt in the same order OC does: visible text first, then hidden
		// instructions appended. The hidden block carries the JSON-schema cue that drives
		// the structured response.
		string composed = string.IsNullOrWhiteSpace(request.HiddenPrompt)
			? request.VisiblePrompt
			: $"{request.VisiblePrompt.TrimEnd()}\n\n{request.HiddenPrompt.Trim()}";

		ChatOptions options = new() { ModelId = model };

		if (logger.IsEnabled(LogLevel.Information)) {
			logger.LogInformation("generation request kind={Kind} chat={Chat} model={Model} visibleChars={Visible} hiddenChars={Hidden}",
				request.Kind, request.ChatExternalId, model, request.VisiblePrompt.Length, request.HiddenPrompt?.Length ?? 0);
		}

		ChatResponse response = await chatClient.GetResponseAsync(composed, options, ct);

		string text         = response.Text ?? "";
		int    inputTokens  = (int)(response.Usage?.InputTokenCount  ?? 0);
		int    outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);

		if (logger.IsEnabled(LogLevel.Information)) {
			logger.LogInformation("generation result kind={Kind} chat={Chat} bodyChars={Body} input={In} output={Out}",
				request.Kind, request.ChatExternalId, text.Length, inputTokens, outputTokens);
		}

		return new GenerationResult(text, inputTokens, outputTokens);
	}
}
