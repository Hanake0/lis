using Grafana.OpenTelemetry;

using Lis.Agent;
using Lis.Channels.WhatsApp;
using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Providers.Anthropic;
using Lis.Providers.Embedding;
using Lis.Providers.OpenAi;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

DotEnv.Load();

// SK telemetry
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics",          true);
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Controllers + JSON serialization
builder.Services.AddControllers()
	   .AddApplicationPart(typeof(GowaWebhookController).Assembly)
	   .AddJsonOptions(opt => JsonOpt.Configure(opt.JsonSerializerOptions));

// Error handling
builder.Services.AddProblemDetails();

// Observability
builder.Services.AddOpenTelemetry()
	   .ConfigureResource(rb => rb.AddService(serviceName: "lis", serviceNamespace: "lis"))
	   .WithMetrics(metrics => {
		   metrics.AddAspNetCoreInstrumentation();
		   metrics.AddHttpClientInstrumentation();
		   metrics.AddRuntimeInstrumentation();
		   metrics.AddMeter("Microsoft.SemanticKernel*");
	   })
	   .WithTracing(tracing => {
		   tracing.AddAspNetCoreInstrumentation();
		   tracing.AddHttpClientInstrumentation(o => o.RecordException = true);
		   tracing.AddSource("Microsoft.SemanticKernel*");
		   tracing.AddSource(TraceAspect.ActivitySource.Name);
	   })
	   .UseGrafana();
builder.Logging.AddOpenTelemetry(logging => {
	logging.IncludeFormattedMessage = true;
	logging.IncludeScopes           = true;
});

// Configuration
builder.Services.AddSingleton(Options.Create(new LisOptions {
												 OwnerJid                = Env("LIS_OWNER_JID"),
												 Timezone                = Env("LIS_TIMEZONE") is { Length: > 0 } t ? t : "E. South America Standard Time",
												 MessageDebounceMs       = EnvInt("LIS_MESSAGE_DEBOUNCE_MS",       3000),
												 ToolNotifications       = Env("LIS_TOOL_NOTIFICATIONS") != "false",
												 KeepRecentTokens        = EnvInt("LIS_KEEP_RECENT_TOKENS",        4000),
												 ToolPruneThreshold      = EnvInt("LIS_TOOL_PRUNE_THRESHOLD",      8000),
												 ToolKeepThreshold       = EnvInt("LIS_TOOL_KEEP_THRESHOLD",       2000),
												 CompactionThreshold     = EnvInt("LIS_COMPACTION_THRESHOLD",      0),
												 CompactionNotify        = Env("LIS_COMPACTION_NOTIFY") != "false",
												 CompactionModel         = Env("LIS_COMPACTION_MODEL"),
												 ToolSummarizationPolicy = Env("LIS_TOOL_SUMMARIZATION_POLICY") is { Length: > 0 } p ? p : "auto",
												 ReactOnMessageQueued      = Env("LIS_REACT_ON_MESSAGE_QUEUED") == "true",
												 ReactOnMessageQueuedEmoji = Env("LIS_REACT_ON_MESSAGE_QUEUED_EMOJI") is { Length: > 0 } e ? e : "🕐",
												 ResumeTokenBudget       = EnvInt("LIS_RESUME_TOKEN_BUDGET",       0),
											 GroupContextMessages    = EnvInt("LIS_GROUP_CONTEXT_MESSAGES",    5),
											 NewSessionOnAgentSwitch = Env("LIS_NEW_SESSION_ON_AGENT_SWITCH") != "false"
											 }));

// Database
if (Env("DATABASE_URL") is { Length: > 0 } dbUrl)
	builder.Configuration["ConnectionStrings:lisdb"] = dbUrl;
builder.AddNpgsqlDbContext<LisDbContext>("lisdb",
										 configureDbContextOptions: options => options.UseNpgsql(o => o.UseVector()));

// Defaults (providers override these)
builder.Services.AddSingleton(new ModelSettings());

// AI Provider
if (Env("ANTHROPIC_ENABLED") == "true") builder.Services.AddAnthropic();

// Audio transcription (optional — audio messages fall back to placeholder without it)
if (Env("OPENAI_API_KEY") is { Length: > 0 }) builder.Services.AddOpenAiTranscription();

// Compaction client (keyed IChatClient for summarization — falls back to main)
if (Env("LIS_COMPACTION_PROVIDER") is { Length: > 0 } compProvider
    && compProvider.Equals("anthropic", StringComparison.OrdinalIgnoreCase)) {
	string compApiKey = Env("LIS_COMPACTION_API_KEY");
	Anthropic.SDK.AnthropicClient compClient = new(compApiKey);
	builder.Services.AddKeyedSingleton<IChatClient>("compaction", compClient.Messages);
} else {
	// Reuse main client for compaction
	builder.Services.AddKeyedSingleton<IChatClient>("compaction",
		(sp, _) => sp.GetRequiredService<IChatClient>());
}

// Embedding (optional — enables vector search for memories)
if (Env("MEMORIES_EMBEDDING_ENABLED") == "true") builder.Services.AddEmbedding();

// Channel
if (Env("GOWA_ENABLED") == "true") builder.Services.AddWhatsApp();

// Application services
builder.Services.AddSingleton<ContextWindowBuilder>();
builder.Services.AddSingleton<PromptComposer>();
builder.Services.AddSingleton<ToolRunner>();

// Conversation + Agent require both AI and Channel
if (Env("ANTHROPIC_ENABLED") == "true" && Env("GOWA_ENABLED") == "true") {
	builder.Services.AddScoped<ConversationService>();
	builder.Services.AddSingleton<IConversationService, MessageDebouncer>();
	builder.Services.AddLisAgent();
}

WebApplication app = builder.Build();

// Apply migrations on startup + seed default agent
using (IServiceScope scope = app.Services.CreateScope()) {
	LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
	await db.Database.MigrateAsync();

	AgentService agentService = scope.ServiceProvider.GetRequiredService<AgentService>();
	ModelSettings envModelSettings = scope.ServiceProvider.GetRequiredService<ModelSettings>();
	LisOptions lisOpts = scope.ServiceProvider.GetRequiredService<IOptions<LisOptions>>().Value;
	await agentService.SeedDefaultAsync(db, envModelSettings, lisOpts, CancellationToken.None);
}

// Flush queued messages from crash recovery
if (app.Services.GetService<IConversationService>() is MessageDebouncer debouncer)
	await debouncer.TriggerPendingResponsesAsync();

// Middleware
app.UseExceptionHandler();

// Endpoints
app.MapControllers();

await app.RunAsync();
return;

// Helpers
static string Env(string key) {
	return Environment.GetEnvironmentVariable(key) ?? "";
}

static int EnvInt(string key, int fallback) {
	return int.TryParse(Environment.GetEnvironmentVariable(key), out int v) ? v : fallback;
}
