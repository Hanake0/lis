namespace Lis.Core.Channel;

public sealed record QuestionResult(IReadOnlyList<IReadOnlyList<string>> Answers);

public interface IQuestionService {
	Task<QuestionResult?> AskAsync(IReadOnlyList<QuestionItem> questions, int timeoutSeconds = 120, CancellationToken ct = default);

	Task<bool> ResolveAsync(string questionId, IReadOnlyList<IReadOnlyList<string>> answers);

	Task<bool> RejectAsync(string questionId);
}
