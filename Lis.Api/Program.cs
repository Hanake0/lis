using Grafana.OpenTelemetry;

using Lis.Agent;
using Lis.Api.Filters;
using Lis.Api.Options;
using Lis.Api.Services;
using Lis.Channels.WhatsApp;
using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Events;
using Lis.Core.Util;
using Lis.Git.Services;
using Lis.Persistence;
using Lis.Persistence.Services;
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

// Cap shutdown wait so a stuck SSE subscriber can't block CTRL+C — the SSE controllers
// link ApplicationStopping into their cancellation tokens, this is a safety net.
builder.Services.Configure<HostOptions>(opt => opt.ShutdownTimeout = TimeSpan.FromSeconds(5));

// Controllers + JSON serialization + typed-exception → ProblemDetails mapping
builder.Services.AddControllers(o => o.Filters.Add<ApiExceptionFilter>())
	   .AddApplicationPart(typeof(GowaWebhookController).Assembly)
	   .AddJsonOptions(opt => JsonOpt.Configure(opt.JsonSerializerOptions));

// Error handling
builder.Services.AddProblemDetails();

// CORS — required so the VS Code webview (origin `vscode-webview://*` on desktop,
// `https://<random>.vscode-cdn.net` on vscode.dev / serve-web) and any
// browser-based test fixtures can hit /api/* directly. Extra origins are read
// from `LIS_CORS_ALLOWED_ORIGINS` (comma-separated) for local dev workflows
// like the `python -m http.server` test-sidebar fixture.
string[] extraOrigins = Env("LIS_CORS_ALLOWED_ORIGINS")
	.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
	.SetIsOriginAllowed(origin => origin.StartsWith("vscode-webview://", StringComparison.OrdinalIgnoreCase)
	                              || origin.EndsWith(".vscode-cdn.net", StringComparison.OrdinalIgnoreCase)
	                              || extraOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
	.AllowAnyMethod()
	.AllowAnyHeader()
	.WithExposedHeaders("Last-Event-ID")));

// Private Network Access — Chromium blocks fetches from a PUBLIC origin
// (https://*.vscode-cdn.net for serve-web / vscode.dev webviews) to a PRIVATE
// loopback address (127.0.0.1 / localhost) unless the server explicitly opts
// in by echoing `Access-Control-Allow-Private-Network: true` on the preflight
// (when the browser sends `Access-Control-Request-Private-Network: true`).
// AspNetCore's built-in CORS middleware doesn't handle PNA, so we splice it in
// before MapControllers — only when the request matches an allowed origin we
// already approved above.
//
// Spec: https://wicg.github.io/private-network-access/
// Allowed-Origin-Echo done in a separate inline middleware after UseCors so
// the response already carries Access-Control-Allow-Origin; we just append PNA.

// Time + event bus (singletons — the bus holds in-memory subscriber state and a replay ring)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEventBus, EventBus>();

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

// Default HttpClient factory (needed by plugins like WebPlugin regardless of channel choice).
builder.Services.AddHttpClient();

// Channels
if (Env("GOWA_ENABLED")        == "true") builder.Services.AddWhatsApp();
if (Env("WEB_CHANNEL_ENABLED") == "true") {
	// SseChannelClient is the web channel's IChannelClient — the agent loop publishes
	// chunks/tools/etc through it onto IEventBus, and ChatStreamController fans them
	// out as SSE per-chat. (The legacy WebSocket controller was removed in the
	// Stage 13 teardown — all client→server chat traffic goes through the REST
	// surface on /api/chats/* now.)
	builder.Services.AddSingleton<SseChannelClient>();
	builder.Services.AddSingleton<IChannelClient>(sp => sp.GetRequiredService<SseChannelClient>());

	// Web-user identity — read from the same env vars the legacy WS layer used so
	// existing deployments don't need a config change for the Stage 13 teardown.
	builder.Services.AddSingleton(Options.Create(new WebUserOptions {
		OwnerId   = Env("WEB_CHANNEL_OWNER_ID")   is { Length: > 0 } o ? o : "web:owner",
		OwnerName = Env("WEB_CHANNEL_OWNER_NAME") is { Length: > 0 } n ? n : null,
	}));
}

// Application services
builder.Services.AddSingleton<ContextWindowBuilder>();
builder.Services.AddSingleton<PromptComposer>();
builder.Services.AddSingleton<ToolRunner>();
builder.Services.AddSingleton<ContextBreakdownService>();
builder.Services.AddSingleton<IContextBreakdownService>(sp => sp.GetRequiredService<ContextBreakdownService>());
builder.Services.AddScoped<IProjectService,       ProjectService>();
builder.Services.AddScoped<ISessionFolderService, SessionFolderService>();
builder.Services.AddScoped<IChatPinService,       ChatPinService>();
builder.Services.AddScoped<IGitIdentityService,   GitIdentityService>();
builder.Services.AddScoped<IScheduledTaskService, ScheduledTaskService>();
// Git is stateless (every call opens its own Repository), so a singleton is fine.
builder.Services.AddSingleton<IGitService, GitService>();
// Scheduler: coordinator is a singleton bridge between controllers and the hosted runner.
builder.Services.AddSingleton<ScheduledTaskCoordinator>();
// Default no-op executor; a real executor (e.g. one that calls IConversationService) can
// override this via a later AddScoped<IScheduledTaskExecutor, MyExecutor>() registration.
builder.Services.AddScoped<IScheduledTaskExecutor, NoOpScheduledTaskExecutor>();
builder.Services.AddHostedService<ScheduledTaskRunner>();
// Multi-run: coordinator is a singleton, dispatcher is scoped (so it can compose other
// scoped services like IConversationService once a real dispatcher is wired).
builder.Services.AddSingleton<IMultiRunCoordinator, MultiRunCoordinator>();
builder.Services.AddScoped<IMultiRunDispatcher, NoOpMultiRunDispatcher>();
// GitHub integration: scoped service that talks to Octokit + persists encrypted PATs via DataProtection.
builder.Services.AddDataProtection();
builder.Services.AddScoped<IGitHubService, GitHubService>();

// Conversation + Agent require AI and at least one channel
bool hasChannel = Env("GOWA_ENABLED") == "true" || Env("WEB_CHANNEL_ENABLED") == "true";
if (Env("ANTHROPIC_ENABLED") == "true" && hasChannel) {
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

// Private Network Access opt-in must be installed BEFORE UseCors so its
// OnStarting callback registers before UseCors short-circuits preflight
// requests. When the CORS layer below approves the origin (response gets
// `Access-Control-Allow-Origin`), we echo `Access-Control-Allow-Private-Network`
// so Chromium permits the public→loopback fetch from https://*.vscode-cdn.net
// to http://127.0.0.1.
app.Use(async (ctx, next) => {
	ctx.Response.OnStarting(() => {
		if (ctx.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
			ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
		return Task.CompletedTask;
	});
	await next();
});

app.UseCors();

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
