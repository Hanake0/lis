namespace Lis.Core.Configuration;

public sealed class LisOptions {
	public string OwnerJid               { get; init; } = "";
	public string Timezone               { get; init; } = "E. South America Standard Time";
	public int    SummarizationThreshold { get; init; } = 30;
	public int    MaxRecentMessages      { get; init; } = 50;
}
