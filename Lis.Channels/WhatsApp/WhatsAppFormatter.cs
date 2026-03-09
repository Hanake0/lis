using System.Text;
using System.Text.RegularExpressions;

using Lis.Core.Channel;

namespace Lis.Channels.WhatsApp;

/// <summary>
/// Converts standard markdown to WhatsApp-native formatting.
/// Code blocks are protected via placeholder swap so their content is never modified.
/// </summary>
public sealed partial class WhatsAppFormatter : IMessageFormatter {

	// ── Compiled Regexes ────────────────────────────────────────────

	[GeneratedRegex(@"```[\s\S]*?```",             RegexOptions.None)]
	private static partial Regex FencedCodeRegex();

	[GeneratedRegex(@"`[^`\n]+`",                  RegexOptions.None)]
	private static partial Regex InlineCodeRegex();

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

		string result = content;
		result = ProtectCodeBlocks(result, out List<string> codeBlocks);
		result = ConvertHeaders(result);
		result = ConvertBold(result);
		result = ConvertStrikethrough(result);
		result = ConvertBulletLists(result);
		result = ConvertHorizontalRules(result);
		result = ConvertTables(result);
		result = ConvertLinks(result);
		result = CollapseBlankLines(result);
		result = RestoreCodeBlocks(result, codeBlocks);
		return result.TrimEnd();
	}

	// ── Pipeline Steps ──────────────────────────────────────────────

	private static string ProtectCodeBlocks(string input, out List<string> codeBlocks) {
		List<string> blocks = [];
		int index = 0;

		// Fenced blocks first (``` ... ```)
		string result = FencedCodeRegex().Replace(input, match => {
			blocks.Add(match.Value);
			return $"\x00CODE{index++}\x00";
		});

		// Then inline code (` ... `)
		result = InlineCodeRegex().Replace(result, match => {
			blocks.Add(match.Value);
			return $"\x00CODE{index++}\x00";
		});

		codeBlocks = blocks;
		return result;
	}

	private static string RestoreCodeBlocks(string input, List<string> codeBlocks) {
		if (codeBlocks.Count == 0) return input;

		string result = input;
		for (int i = 0; i < codeBlocks.Count; i++)
			result = result.Replace($"\x00CODE{i}\x00", codeBlocks[i]);

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

			// If link text equals URL, just show the URL
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

		// Parse header row
		string[] headers = ParseTableRow(lines[0]);
		if (headers.Length == 0) return tableText;

		// Find where data rows start (skip separator row like |---|---|)
		int dataStart = 1;
		if (dataStart < lines.Length && IsSeparatorRow(lines[dataStart]))
			dataStart++;

		if (dataStart >= lines.Length) return tableText;

		StringBuilder sb = new();

		for (int i = dataStart; i < lines.Length; i++) {
			string[] cells = ParseTableRow(lines[i]);
			if (cells.Length == 0) continue;

			// Use first column as label (bold), remaining as key-value bullets
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
				// Single-column table: just list values as bullets
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

		// Strip leading/trailing pipe, split by pipe, trim each cell
		string inner = trimmed[1..^1];
		string[] cells = inner.Split('|');

		for (int i = 0; i < cells.Length; i++)
			cells[i] = cells[i].Trim();

		return cells;
	}

	private static bool IsSeparatorRow(string line) {
		string trimmed = line.Trim();
		if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|')) return false;

		// Separator rows contain only |, -, :, and spaces
		for (int i = 1; i < trimmed.Length - 1; i++) {
			char c = trimmed[i];
			if (c is not ('|' or '-' or ':' or ' ')) return false;
		}
		return true;
	}
}
