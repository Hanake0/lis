using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Questions;

/// <summary>
/// Response to a pending tool-question prompt. Either <c>rejected = true</c>
/// (user dismissed the prompt — the tool gets a rejection signal) or
/// <c>answers</c> populated (one inner array per question item: the user's
/// chosen options for that question). Both null/empty answers and a missing
/// answers field are treated as "no selection" (the legacy WS frame default).
/// </summary>
public sealed record RespondToQuestionRequest(
	[property: JsonPropertyName("answers")]   List<List<string>>? Answers,
	[property: JsonPropertyName("rejected")]  bool?               Rejected
);
