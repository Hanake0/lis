using System.Collections.Concurrent;
using System.Security.Cryptography;

using Lis.Core.Channel;
using Lis.Core.Util;

namespace Lis.Agent;

public sealed class QuestionService : IQuestionService {

	private readonly ConcurrentDictionary<string, TaskCompletionSource<QuestionResult?>> _pending = new();

	[Trace("QuestionService > AskAsync")]
	public async Task<QuestionResult?> AskAsync(IReadOnlyList<QuestionItem> questions, int timeoutSeconds = 120, CancellationToken ct = default) {
		if (ToolContext.Channel is null || ToolContext.ChatId is null)
			return null;

		string questionId = GenerateShortId();

		TaskCompletionSource<QuestionResult?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		this._pending[questionId] = tcs;

		try {
			bool interactive = await ToolContext.Channel.SendQuestionAsync(
				ToolContext.ChatId, questionId, questions, ct);

			if (!interactive) {
				this._pending.TryRemove(questionId, out _);
				return null;
			}

			using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(timeoutSeconds));
			using CancellationTokenSource linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

			linkedCts.Token.Register(() => tcs.TrySetResult(null));
			return await tcs.Task;
		} finally {
			this._pending.TryRemove(questionId, out _);
		}
	}

	public Task<bool> ResolveAsync(string questionId, IReadOnlyList<IReadOnlyList<string>> answers) {
		if (!this._pending.TryGetValue(questionId, out TaskCompletionSource<QuestionResult?>? tcs))
			return Task.FromResult(false);

		return Task.FromResult(tcs.TrySetResult(new QuestionResult(answers)));
	}

	public Task<bool> RejectAsync(string questionId) {
		if (!this._pending.TryGetValue(questionId, out TaskCompletionSource<QuestionResult?>? tcs))
			return Task.FromResult(false);

		return Task.FromResult(tcs.TrySetResult(null));
	}

	private static string GenerateShortId() {
		Span<byte> bytes = stackalloc byte[4];
		RandomNumberGenerator.Fill(bytes);
		return $"q-{Convert.ToHexString(bytes).ToLowerInvariant()}";
	}
}
