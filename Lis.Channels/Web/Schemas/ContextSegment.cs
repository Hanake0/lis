using System.Text.Json.Serialization;

namespace Lis.Channels.Web.Schemas;

public sealed class ContextSegment {
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("tokens")]
	public required int Tokens { get; init; }
}
