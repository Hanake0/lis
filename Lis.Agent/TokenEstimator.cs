using Microsoft.ML.Tokenizers;

namespace Lis.Agent;

/// <summary>
/// Local BPE token counter for proportional distribution of API delta tokens.
/// Uses o200k_base (most recent encoding) — exact model match is not required
/// since the API delta provides the ground truth total.
/// </summary>
internal static class TokenEstimator {
	private static readonly TiktokenTokenizer Tok =
		TiktokenTokenizer.CreateForEncoding("o200k_base");

	public static int Count(string? text) =>
		text is { Length: > 0 } ? Tok.CountTokens(text) : 0;
}
