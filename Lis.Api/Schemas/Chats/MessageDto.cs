using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lis.Api.Schemas.Chats;

/// <summary>
/// One historical row in a chat's transcript. Built from <c>MessageEntity</c> by
/// pulling out the live token usage + reaching into the persisted
/// <c>SkContent</c> JSON for tool-call metadata + accumulated thinking text. The
/// shape is consumed unchanged by the OC chat-view (token strip, tool blocks,
/// thinking placeholders) — preserved from the legacy WS frame so the renderer
/// keeps working through the transport swap.
/// </summary>
public sealed record MessageDto(
	[property: JsonPropertyName("id")]                    string         Id,
	[property: JsonPropertyName("role")]                  string         Role,
	[property: JsonPropertyName("text")]                  string?        Text,
	[property: JsonPropertyName("timestamp")]             DateTimeOffset Timestamp,
	[property: JsonPropertyName("input_tokens")]          int?           InputTokens,
	[property: JsonPropertyName("output_tokens")]         int?           OutputTokens,
	[property: JsonPropertyName("cache_read_tokens")]     int?           CacheReadTokens,
	[property: JsonPropertyName("cache_creation_tokens")] int?           CacheCreationTokens,
	[property: JsonPropertyName("thinking_tokens")]       int?           ThinkingTokens,
	[property: JsonPropertyName("tool_name")]             string?        ToolName,
	[property: JsonPropertyName("tool_args")]             string?        ToolArgs,
	[property: JsonPropertyName("tool_is_error")]         bool?          ToolIsError,
	[property: JsonPropertyName("thinking_content")]      string?        ThinkingContent
) {
	/// <summary>
	/// Build a DTO from a flat row materialised by the controller. Splitting like
	/// this keeps the EF projection translatable (no JSON parsing inside the
	/// LINQ expression — that runs on raw strings after the round-trip).
	/// </summary>
	public static MessageDto FromRaw(MessageRowRaw raw) => new(
		Id:                  raw.Id,
		Role:                raw.Role,
		Text:                raw.Text,
		Timestamp:           raw.Timestamp,
		InputTokens:         raw.InputTokens,
		OutputTokens:        raw.OutputTokens,
		CacheReadTokens:     raw.CacheReadTokens,
		CacheCreationTokens: raw.CacheCreationTokens,
		ThinkingTokens:      raw.ThinkingTokens,
		ToolName:            raw.Role == "tool"      ? SkContentExtract.ToolName(raw.SkContent)    : null,
		ToolArgs:            raw.Role == "tool"      ? SkContentExtract.ToolArgs(raw.SkContent)    : null,
		ToolIsError:         raw.Role == "tool"      ? SkContentExtract.ToolIsError(raw.SkContent) : null,
		ThinkingContent:     raw.Role == "assistant" ? SkContentExtract.Thinking(raw.SkContent)    : null
	);
}

/// <summary>
/// Flat in-memory shape projected out of EF before any JSON parsing on
/// <c>SkContent</c>. Kept as a non-JSON record so it can travel through the
/// LINQ-to-Entities translator.
/// </summary>
public sealed record MessageRowRaw(
	string         Id,
	string         Role,
	string?        Text,
	DateTimeOffset Timestamp,
	int?           InputTokens,
	int?           OutputTokens,
	int?           CacheReadTokens,
	int?           CacheCreationTokens,
	int?           ThinkingTokens,
	string?        SkContent
);

/// <summary>
/// Helpers for pulling structured fields out of the persisted Semantic Kernel
/// content JSON. Defensive: any parsing failure returns null so a partially
/// malformed history still renders.
/// </summary>
internal static class SkContentExtract {

	public static string? ToolName(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			// FunctionResultContent serialises with a top-level FunctionName.
			if (doc.RootElement.TryGetProperty("FunctionName", out JsonElement fn))
				return fn.GetString();
			// Fallback: nested Items array (some SK content shapes wrap the call).
			if (doc.RootElement.TryGetProperty("Items", out JsonElement items) && items.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement item in items.EnumerateArray()) {
					if (item.TryGetProperty("FunctionName", out JsonElement itemFn))
						return itemFn.GetString();
				}
			}
		} catch (JsonException) { /* malformed — return null */ }
		return null;
	}

	/// <summary>
	/// Reads <c>Metadata.ToolArgs</c> — populated by ToolRunner so history can
	/// show the args the agent passed to the function.
	/// </summary>
	public static string? ToolArgs(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("ToolArgs", out JsonElement args))
				return args.GetString();
		} catch (JsonException) { /* malformed — return null */ }
		return null;
	}

	/// <summary>
	/// Reads <c>Metadata.IsError</c> — populated by ToolRunner so history can
	/// render failed calls in their original failed state.
	/// </summary>
	public static bool? ToolIsError(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("IsError", out JsonElement flag)
			 && (flag.ValueKind == JsonValueKind.True || flag.ValueKind == JsonValueKind.False))
				return flag.GetBoolean();
		} catch (JsonException) { /* malformed — return null */ }
		return null;
	}

	/// <summary>
	/// Reads <c>Metadata.LisThinking</c> — accumulated thinking text streamed
	/// live, persisted so the placeholder block on history reload shows exactly
	/// what the user saw.
	/// </summary>
	public static string? Thinking(string? skContent) {
		if (string.IsNullOrEmpty(skContent)) return null;
		try {
			using JsonDocument doc = JsonDocument.Parse(skContent);
			if (doc.RootElement.TryGetProperty("Metadata", out JsonElement meta)
			 && meta.TryGetProperty("LisThinking", out JsonElement think)
			 && think.ValueKind == JsonValueKind.String)
				return think.GetString();
		} catch (JsonException) { /* malformed — return null */ }
		return null;
	}
}
