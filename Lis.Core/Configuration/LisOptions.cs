namespace Lis.Core.Configuration;

public sealed class LisOptions {
	public string OwnerJid                { get; init; } = "";
	public string Timezone                { get; init; } = "E. South America Standard Time";
	public int    MessageDebounceMs       { get; init; } = 3000;
	public bool   ToolNotifications       { get; init; } = true;

	// Compaction (0 = percentage of ContextBudget)
	public int    KeepRecentTokens        { get; init; } = 4000;
	public int    ToolPruneThreshold      { get; init; } = 8000;
	public int    ToolKeepThreshold       { get; init; } = 2000;
	public int    CompactionThreshold     { get; init; }           // 0 → 80% of ContextBudget
	public bool   CompactionNotify        { get; init; } = true;
	public string CompactionModel         { get; init; } = "";     // empty = use main model
	public string ToolSummarizationPolicy { get; init; } = "auto"; // auto, keep_all, keep_none

	// Queue
	public bool   ReactOnMessageQueued      { get; init; }
	public string ReactOnMessageQueuedEmoji { get; init; } = "🕐";

	// Resume
	public int    ResumeTokenBudget       { get; init; }           // 0 → 70% of ContextBudget

	// Agent
	public bool   NewSessionOnAgentSwitch { get; init; } = true;
}
