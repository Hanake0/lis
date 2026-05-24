using System.ComponentModel;
using System.Text;
using System.Text.Json;

using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class QuestionPlugin(IQuestionService questionService) {

	[KernelFunction("ask_user")]
	[Description(
		"Ask the user one or more structured questions with predefined options. " +
		"Use when you need the user's preference, a decision between alternatives, or clarification. " +
		"Each question can have a short header label, a body, and a list of options with descriptions. " +
		"Set multiple=true to allow selecting more than one option. " +
		"The user can always provide a custom free-text answer instead of picking an option.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.Open)]
	public async Task<string> AskUserAsync(
		[Description("JSON array of question objects. Each has: question (string, required), header (string, short label), options (array of {label, description}), multiple (bool, allow multi-select)")] string questionsJson,
		[Description("Seconds to wait for user response (default 120, max 300)")] int timeoutSeconds = 120) {

		List<QuestionItem> items;
		try {
			items = ParseQuestions(questionsJson);
		} catch (Exception ex) {
			return $"Error parsing questions: {ex.Message}";
		}

		if (items.Count == 0)
			return "Error: at least one question is required.";

		timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 300);

		QuestionResult? result = await questionService.AskAsync(items, timeoutSeconds);

		if (result is null)
			return "The user dismissed the question or it timed out.";

		return FormatAnswers(items, result.Answers);
	}

	private static List<QuestionItem> ParseQuestions(string json) =>
		JsonSerializer.Deserialize<List<QuestionItem>>(json, JsonOpt.Default)
		?? throw new InvalidOperationException("Failed to deserialize questions");

	private static string FormatAnswers(List<QuestionItem> items, IReadOnlyList<IReadOnlyList<string>> answers) {
		if (items.Count == 1 && answers.Count == 1)
			return string.Join(", ", answers[0]);

		StringBuilder sb = new();
		for (int i = 0; i < items.Count && i < answers.Count; i++) {
			string header = items[i].Header ?? $"Question {i + 1}";
			string answer = answers[i].Count > 0 ? string.Join(", ", answers[i]) : "(no answer)";
			sb.AppendLine($"{header}: {answer}");
		}
		return sb.ToString().TrimEnd();
	}
}
