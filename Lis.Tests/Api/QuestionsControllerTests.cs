using Lis.Api.Controllers;
using Lis.Api.Schemas.Questions;
using Lis.Core.Channel;

using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="QuestionsController"/>. A capturing
/// <see cref="IQuestionService"/> records every resolve/reject call so we
/// can assert the answers/rejected branching and the null-coalesce behaviour
/// of the legacy WS frame is preserved.
/// </summary>
public class QuestionsControllerTests {
	private readonly CapturingQuestionService questions = new();
	private readonly QuestionsController      sut;

	public QuestionsControllerTests() {
		this.sut = new QuestionsController(this.questions);
	}

	[Fact]
	public async Task Respond_With_Answers_Calls_ResolveAsync_With_The_Answer_Matrix() {
		this.questions.NextResolve = true;
		IActionResult result = await this.sut.Respond("q-1", new RespondToQuestionRequest(
			Answers: [["yes"], ["one", "two"]],
			Rejected: false
		), default);

		Assert.IsType<NoContentResult>(result);
		(string id, IReadOnlyList<IReadOnlyList<string>>? ans) = Assert.Single(this.questions.ResolveCalls);
		Assert.Equal("q-1", id);
		Assert.NotNull(ans);
		Assert.Equal(2, ans!.Count);
		Assert.Equal(["yes"],         ans[0]);
		Assert.Equal(["one", "two"],  ans[1]);
		Assert.Empty(this.questions.RejectCalls);
	}

	[Fact]
	public async Task Respond_With_Null_Answers_Defaults_To_Empty_List_Matching_The_Legacy_WS_Frame() {
		this.questions.NextResolve = true;
		await this.sut.Respond("q-1", new RespondToQuestionRequest(Answers: null, Rejected: null), default);
		(_, IReadOnlyList<IReadOnlyList<string>>? ans) = Assert.Single(this.questions.ResolveCalls);
		Assert.NotNull(ans);
		Assert.Empty(ans!);
	}

	[Fact]
	public async Task Respond_With_Rejected_True_Calls_RejectAsync_Instead() {
		this.questions.NextReject = true;
		IActionResult result = await this.sut.Respond("q-1", new RespondToQuestionRequest(Answers: null, Rejected: true), default);

		Assert.IsType<NoContentResult>(result);
		string id = Assert.Single(this.questions.RejectCalls);
		Assert.Equal("q-1", id);
		Assert.Empty(this.questions.ResolveCalls);
	}

	[Fact]
	public async Task Respond_Missing_Question_Throws_KeyNotFoundException_On_Resolve_Path() {
		this.questions.NextResolve = false;
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Respond("q-ghost", new RespondToQuestionRequest(Answers: [], Rejected: false), default));
	}

	[Fact]
	public async Task Respond_Missing_Question_Throws_KeyNotFoundException_On_Reject_Path() {
		this.questions.NextReject = false;
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Respond("q-ghost", new RespondToQuestionRequest(Answers: null, Rejected: true), default));
	}

	private sealed class CapturingQuestionService :IQuestionService {
		public List<(string id, IReadOnlyList<IReadOnlyList<string>>? answers)> ResolveCalls { get; } = [];
		public List<string> RejectCalls { get; } = [];
		public bool NextResolve { get; set; }
		public bool NextReject  { get; set; }

		public Task<bool> ResolveAsync(string questionId, IReadOnlyList<IReadOnlyList<string>> answers) {
			this.ResolveCalls.Add((questionId, answers));
			return Task.FromResult(this.NextResolve);
		}

		public Task<bool> RejectAsync(string questionId) {
			this.RejectCalls.Add(questionId);
			return Task.FromResult(this.NextReject);
		}

		public Task<QuestionResult?> AskAsync(IReadOnlyList<QuestionItem> questions, int timeoutSeconds = 120, CancellationToken ct = default)
			=> throw new NotSupportedException("not used by controller tests");
	}
}
