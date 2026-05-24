using System.Text.Json.Serialization;

namespace Lis.Core.Channel;

public sealed record QuestionOption(
	[property: JsonPropertyName("label")] string Label,
	[property: JsonPropertyName("description")] string? Description = null);

public sealed record QuestionItem(
	[property: JsonPropertyName("question")] string Question,
	[property: JsonPropertyName("header")] string? Header = null,
	[property: JsonPropertyName("options")] IReadOnlyList<QuestionOption>? Options = null,
	[property: JsonPropertyName("multiple")] bool Multiple = false);
