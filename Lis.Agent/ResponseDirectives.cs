namespace Lis.Agent;

public static class ResponseDirectives {
	private const string QuotePrefix    = "[QUOTE]";
	private const string NoResponseBody = "NO_RESPONSE";

	/// <summary>
	/// Parses response directives from the model's text output.
	/// Returns the cleaned content (null if NO_RESPONSE) and whether to quote.
	/// </summary>
	public static (string? Content, bool ShouldQuote) Parse(string? raw) {
		if (string.IsNullOrWhiteSpace(raw)) return (null, false);

		string trimmed = raw.Trim();

		if (trimmed == NoResponseBody) return (null, false);

		if (trimmed.StartsWith(QuotePrefix, StringComparison.Ordinal)) {
			string after = trimmed[QuotePrefix.Length..];
			if (after.Length == 0) return (null, true);

			// Strip leading newline or space after [QUOTE]
			if (after[0] is '\n' or ' ')
				after = after[1..];

			return (after.Length > 0 ? after : null, true);
		}

		return (raw, false);
	}
}
