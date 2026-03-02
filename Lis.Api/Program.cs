using Lis.Agent;
using Lis.Channels.WhatsApp;
using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Providers.Anthropic;
using Lis.Providers.Embedding;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

DotEnv.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Controllers + JSON serialization
builder.Services.AddControllers()
	   .AddApplicationPart(typeof(GowaWebhookController).Assembly)
	   .AddJsonOptions(opt => JsonOpt.Configure(opt.JsonSerializerOptions));

// Error handling
builder.Services.AddProblemDetails();

// Configuration
builder.Services.AddSingleton(Options.Create(new LisOptions {
												 OwnerJid               = Env("LIS_OWNER_JID"),
												 Timezone               = Env("LIS_TIMEZONE") is { Length: > 0 } t ? t : "E. South America Standard Time",
												 MaxRecentMessages      = EnvInt("LIS_MAX_RECENT_MESSAGES",     50),
												 SummarizationThreshold = EnvInt("LIS_SUMMARIZATION_THRESHOLD", 30),
												 MessageDebounceMs      = EnvInt("LIS_MESSAGE_DEBOUNCE_MS",     3000)
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

// Embedding (optional — enables vector search for memories)
if (Env("MEMORIES_EMBEDDING_ENABLED") == "true") builder.Services.AddEmbedding();

// Channel
if (Env("GOWA_ENABLED") == "true") builder.Services.AddWhatsApp();

// Application services
builder.Services.AddSingleton<ContextWindowBuilder>();
builder.Services.AddSingleton<PromptComposer>();

// Conversation + Agent require both AI and Channel
if (Env("ANTHROPIC_ENABLED") == "true" && Env("GOWA_ENABLED") == "true") {
	builder.Services.AddScoped<ConversationService>();
	builder.Services.AddSingleton<IConversationService, MessageDebouncer>();
	builder.Services.AddLisAgent();
}

WebApplication app = builder.Build();

// Apply migrations on startup
using (IServiceScope scope = app.Services.CreateScope()) {
	LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
	await db.Database.MigrateAsync();
}

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
