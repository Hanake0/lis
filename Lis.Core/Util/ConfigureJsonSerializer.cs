using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Lis.Core.Util;

public static class JsonOpt {
	private static readonly JsonStringEnumConverter EnumConverter = new(JsonNamingPolicy.SnakeCaseLower);

	public static readonly JsonSerializerOptions Default = new() {
		Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),

		// Do not include fields with null values
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,

		// Handle circular references in entity navigation properties
		ReferenceHandler = ReferenceHandler.IgnoreCycles,

		// Serialize enums as lowercase strings
		Converters = { EnumConverter },

		// Ignore any inconsistencies
		AllowTrailingCommas         = true,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling         = JsonCommentHandling.Skip,
		UnknownTypeHandling         = JsonUnknownTypeHandling.JsonElement
	};

	public static void Configure(JsonSerializerOptions options) {
		options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

		// Do not include fields with null values
		options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
		options.PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower;

		// Handle circular references in entity navigation properties
		options.ReferenceHandler = ReferenceHandler.IgnoreCycles;

		// Serialize enums as lowercase strings
		options.Converters.Add(EnumConverter);

		// Ignore any inconsistencies in AI generated JSON
		options.AllowTrailingCommas         = true;
		options.PropertyNameCaseInsensitive = true;
		options.ReadCommentHandling         = JsonCommentHandling.Skip;
		options.UnknownTypeHandling         = JsonUnknownTypeHandling.JsonElement;
	}
}
