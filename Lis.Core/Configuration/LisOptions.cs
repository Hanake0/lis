namespace Lis.Core.Configuration;

public sealed class LisOptions {
	public string OwnerJid                { get; init; } = "";
	public string Timezone                { get; init; } = "E. South America Standard Time";
	public int    MaxRecentMessages       { get; init; } = 50;
	public int    MessageDebounceMs       { get; init; } = 3000;
	public bool   ToolNotifications       { get; init; } = true;

	// Compaction
	public int    KeepRecentTokens        { get; init; } = 4000;
	public int    ToolPruneThreshold      { get; init; } = 8000;
	public int    ToolKeepThreshold       { get; init; } = 2000;
	public int    CompactionThreshold     { get; init; } = 10000;
	public bool   CompactionNotify        { get; init; } = true;
	public string CompactionModel         { get; init; } = "";     // empty = use main model
	public string ToolSummarizationPolicy { get; init; } = "auto"; // auto, keep_all, keep_none
}
