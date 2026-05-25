using Lis.Api.Schemas.Questions;
using Lis.Core.Channel;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Api.Controllers;

/// <summary>
/// Resolves pending tool-question prompts. Tools that need user input (e.g.
/// the SetReminderTool's confirm-time question) park on
/// <see cref="IQuestionService"/> until the user submits answers or
/// explicitly rejects; this endpoint delivers either outcome.
/// </summary>
[ApiController]
[Route("api/questions")]
[Tags("Questions")]
[Produces("application/json")]
public sealed class QuestionsController(IQuestionService questions) :ControllerBase {

	[HttpPost("{questionId}/respond")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> Respond(
		[FromRoute] string                      questionId,
		[FromBody]  RespondToQuestionRequest    request,
		CancellationToken                       ct
	) {
		if (request is null) throw new ArgumentException("body is required", nameof(request));
		_ = ct;

		bool resolved;
		if (request.Rejected == true) {
			resolved = await questions.RejectAsync(questionId);
		} else {
			// Null/empty answers → resolve with an empty selection list (one inner
			// array per question item, all empty) — matches the legacy WS frame.
			List<IReadOnlyList<string>> answers = request.Answers?
				.Select(a => (IReadOnlyList<string>)a.AsReadOnly())
				.ToList() ?? [];
			resolved = await questions.ResolveAsync(questionId, answers);
		}

		if (!resolved) throw new KeyNotFoundException($"No pending question with ID '{questionId}'");
		return this.NoContent();
	}
}
