using System.Text;
using System.Text.RegularExpressions;

using Lis.Core.Channel;

namespace Lis.Channels.WhatsApp;

/// <summary>
/// Converts standard markdown to WhatsApp-native formatting.
/// Code blocks are left untouched — only non-code segments are formatted.
/// </summary>
public sealed partial class WhatsAppFormatter : IMessageFormatter {

	// ── Compiled Regexes ────────────────────────────────────────────

	[GeneratedRegex(@"```[\s\S]*?```|`[^`\n]+`", RegexOptions.None)]
	private static partial Regex CodeBlockRegex();

	[GeneratedRegex(@"^#{1,6}\s+(.+)$",            RegexOptions.Multiline)]
	private static partial Regex HeaderRegex();

	[GeneratedRegex(@"\*\*(.+?)\*\*",              RegexOptions.Singleline)]
	private static partial Regex BoldRegex();

	[GeneratedRegex(@"~~(.+?)~~",                   RegexOptions.Singleline)]
	private static partial Regex StrikethroughRegex();

	[GeneratedRegex(@"^[\-\*]\s+",                  RegexOptions.Multiline)]
	private static partial Regex BulletRegex();

	[GeneratedRegex(@"^[-\*_]{3,}\s*$",             RegexOptions.Multiline)]
	private static partial Regex HorizontalRuleRegex();

	[GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)",     RegexOptions.None)]
	private static partial Regex LinkRegex();

	[GeneratedRegex(@"\n{3,}",                      RegexOptions.None)]
	private static partial Regex ExcessiveNewlinesRegex();

	// Matches a full markdown table (header row, separator row, data rows)
	[GeneratedRegex(@"(?:^\|.+\|[ \t]*\n)*^\|.+\|[ \t]*$", RegexOptions.Multiline)]
	private static partial Regex TableBlockRegex();

	// ── Public API ──────────────────────────────────────────────────

	public string Format(string content) {
		if (string.IsNullOrWhiteSpace(content)) return string.Empty;

		// Split into code and non-code segments, only format non-code parts
		StringBuilder     sb      = new();
		MatchCollection   matches = CodeBlockRegex().Matches(content);
		int               cursor  = 0;

		foreach (Match match in matches) {
			if (match.Index > cursor)
				sb.Append(FormatText(content[cursor..match.Index]));

			sb.Append(match.Value);
			cursor = match.Index + match.Length;
		}

		if (cursor < content.Length)
			sb.Append(FormatText(content[cursor..]));

		return sb.ToString().TrimEnd();
	}

	// ── Formatting (applied only to non-code segments) ──────────────

	private static string FormatText(string input) {
		string result = input;
		result = ConvertHeaders(result);
		result = ConvertBold(result);
		result = ConvertStrikethrough(result);
		result = ConvertBulletLists(result);
		result = ConvertHorizontalRules(result);
		result = ConvertTables(result);
		result = ConvertLinks(result);
		result = CollapseBlankLines(result);
		return result;
	}

	private static string ConvertHeaders(string input) =>
		HeaderRegex().Replace(input, match => $"*{match.Groups[1].Value.Trim()}*");

	private static string ConvertBold(string input) =>
		BoldRegex().Replace(input, match => $"*{match.Groups[1].Value}*");

	private static string ConvertStrikethrough(string input) =>
		StrikethroughRegex().Replace(input, match => $"~{match.Groups[1].Value}~");

	private static string ConvertBulletLists(string input) =>
		BulletRegex().Replace(input, "• ");

	private static string ConvertHorizontalRules(string input) =>
		HorizontalRuleRegex().Replace(input, "───");

	private static string ConvertLinks(string input) =>
		LinkRegex().Replace(input, match => {
			string text = match.Groups[1].Value;
			string url  = match.Groups[2].Value;

			if (text == url) return url;
			return $"{text} ({url})";
		});

	private static string CollapseBlankLines(string input) =>
		ExcessiveNewlinesRegex().Replace(input, "\n\n");

	// ── Table Conversion ────────────────────────────────────────────

	private static string ConvertTables(string input) =>
		TableBlockRegex().Replace(input, match => ConvertTableBlock(match.Value));

	private static string ConvertTableBlock(string tableText) {
		string[] lines = tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length < 2) return tableText;

		string[] headers = ParseTableRow(lines[0]);
		if (headers.Length == 0) return tableText;

		int dataStart = 1;
		if (dataStart < lines.Length && IsSeparatorRow(lines[dataStart]))
			dataStart++;

		if (dataStart >= lines.Length) return tableText;

		StringBuilder sb = new();

		for (int i = dataStart; i < lines.Length; i++) {
			string[] cells = ParseTableRow(lines[i]);
			if (cells.Length == 0) continue;

			if (headers.Length > 1 && cells.Length > 0) {
				sb.Append('*').Append(cells[0]).AppendLine("*");
				for (int j = 1; j < Math.Max(headers.Length, cells.Length); j++) {
					string header = j < headers.Length ? headers[j] : $"Column {j}";
					string value  = j < cells.Length ? cells[j] : "";
					if (string.IsNullOrWhiteSpace(value)) continue;
					sb.Append("• ").Append(header).Append(": ").AppendLine(value);
				}
				sb.AppendLine();
			} else {
				for (int j = 0; j < cells.Length; j++) {
					if (string.IsNullOrWhiteSpace(cells[j])) continue;
					string header = j < headers.Length ? headers[j] : "";
					if (!string.IsNullOrWhiteSpace(header))
						sb.Append("• ").Append(header).Append(": ").AppendLine(cells[j]);
					else
						sb.Append("• ").AppendLine(cells[j]);
				}
				sb.AppendLine();
			}
		}

		return sb.ToString();
	}

	private static string[] ParseTableRow(string line) {
		string trimmed = line.Trim();
		if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|')) return [];

		string inner = trimmed[1..^1];
		string[] cells = inner.Split('|');

		for (int i = 0; i < cells.Length; i++)
			cells[i] = cells[i].Trim();

		return cells;
	}

	private static bool IsSeparatorRow(string line) {
		string trimmed = line.Trim();
		if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|')) return false;

		for (int i = 1; i < trimmed.Length - 1; i++) {
			char c = trimmed[i];
			if (c is not ('|' or '-' or ':' or ' ')) return false;
		}
		return true;
	}
}
