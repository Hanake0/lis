namespace Lis.Core.Channel;

public sealed record QuestionOption(string Label, string? Description = null);

public sealed record QuestionItem(
	string Question,
	string? Header = null,
	IReadOnlyList<QuestionOption>? Options = null,
	bool Multiple = false);
