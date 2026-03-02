## Project Overview

Lis - Personal AI assistant with provider-agnostic messaging and AI, powered by Semantic Kernel.

## Commands

```bash
# Development
cd Lis.Api && dotnet run

# Build
dotnet build

# Run migrations
dotnet ef database update --project Lis.Persistence/Lis.Persistence.csproj --startup-project Lis.Api/Lis.Api.csproj

# Add migration (use snake_case names)
dotnet ef migrations add <migration_name> --project Lis.Persistence/Lis.Persistence.csproj --startup-project Lis.Api/Lis.Api.csproj

# Run tests
dotnet test Lis.Tests/Lis.Tests.csproj

# Code cleanup (from repo root)
jb cleanupcode Lis.Api/Lis.Api.csproj --profile="Built-in: Full Cleanup" --settings=Lis.sln.DotSettings
```

## Architecture

- **Framework**: ASP.NET Core (.NET 10)
- **Database**: PostgreSQL via Entity Framework Core
- **AI**: Microsoft Semantic Kernel (provider-agnostic via IChatClient)
- **Messaging**: Provider-agnostic via IChannelClient
- **Observability**: OpenTelemetry + Grafana

### Solution Structure

```
Lis.Core          — Interfaces, configuration (ModelSettings, LisOptions), utilities
Lis.Persistence   — EF Core DbContext, entities, migrations
Lis.Agent         — Semantic Kernel setup, conversation orchestration, context window
Lis.Tools         — Agent plugins (DateTimePlugin, future: Memory, Reminders, Shell, etc.)
Lis.Providers     — AI provider implementations (Anthropic/, future: OpenAi/, etc.)
Lis.Channels      — Channel implementations (WhatsApp/, future: Telegram/, Discord/, etc.)
Lis.Api           — ASP.NET Core host (composition root), Dockerfile
Lis.Tests         — Unit tests
```

### Dependency Graph

```
Lis.Core  (lightweight - no heavy deps)
    ↑
    ├── Lis.Persistence  (→ Core, EF Core, Npgsql)
    ├── Lis.Tools        (→ Core, Semantic Kernel)
    ├── Lis.Providers    (→ Core, Anthropic SDK)
    ├── Lis.Channels     (→ Core, Http.Resilience)
    │
    └── Lis.Agent        (→ Core, Persistence, Tools, Semantic Kernel)
            ↑
            Lis.Api      (→ all above, ASP.NET Core)
```

### Provider Architecture

AI providers and channels follow the same pattern:
- Each has an `Add*()` extension method that reads its own env vars and registers services
- Conditional registration via `*_ENABLED` env var
- Provider registers `IChatClient` + `ModelSettings`; channel registers `IChannelClient`

```csharp
if (Env("ANTHROPIC_ENABLED") == "true") builder.Services.AddAnthropic();
if (Env("GOWA_ENABLED") == "true") builder.Services.AddWhatsApp();
```

### Configuration

All config uses flat `.env` variables (loaded via `DotEnv.Load()`). See `.env.example` for all keys.
Each provider/channel reads its own `*_ENABLED`, `*_API_KEY`, etc. env vars internally in its `Add*()` method.
Options classes wrap env vars for type safety and are registered via `Options.Create()`.

### Channel Abstraction

- `IChannelClient` (Lis.Core) — provider-agnostic messaging interface
- `IncomingMessage` (Lis.Core) — common incoming message model
- `IConversationService` (Lis.Core) — conversation orchestration interface
- Each channel adapter maps its native payload → `IncomingMessage` before calling `IConversationService`

### Schemas Pattern

Schemas are DTOs for external API payloads. Located in `{channel}/Schemas/`.

- Use `[JsonPropertyName("snake_case")]` for JSON serialization
- Use `[Required]`, `[MaxLength]`, etc for validation

### Controller Conventions

- Use `[ApiController]`, `[Route("resource")]`, `[Tags("TagName")]`
- Use `[ProducesResponseType]` attributes for all response types
- Primary constructors for dependency injection
- Return `IActionResult` using `this.Ok()`, `this.Unauthorized()`, `this.BadRequest()`, etc.
- Use `[FromRoute]` and `[FromBody]` attributes explicitly
- Snake_case JSON serialization via `JsonOpt.Configure()`
- ProblemDetails for error responses
- Controllers in class libraries need `.AddApplicationPart()` in Program.cs

### Agent Rules

- **You own every change.** Whether you wrote it, a formatter touched it, or a tool modified it — if you ran the command, you are responsible for the result. Never dismiss side effects as "not my fault."
- **Never remove or modify code you don't understand.** If something breaks the build but you didn't write it, ASK before deleting. It's likely in-progress work.
- **Do exactly what the user asked.** Don't sidestep, skip steps, or partially complete requests. If the user says "run X", run X and verify the output — don't declare it done without checking.
- **Verify after every destructive or formatting tool.** After running formatters, linters, or code cleanup: diff the result, confirm only intended files changed, and fix any collateral damage immediately.
- **Never use `dotnet format`.** Use `jb cleanupcode` as specified below. This is non-negotiable.
- **No half-fixes.** Find and fix the actual underlying problem. A half-assed fix is worse than no fix at all. Don't paper over symptoms — understand the root cause first.

### Code Style

- **Formatting**: Always run ReSharper Full Cleanup after making changes (from repo root):
  ```bash
  jb cleanupcode Lis.Api/Lis.Api.csproj --profile="Built-in: Full Cleanup" --settings=Lis.sln.DotSettings
  ```
- Use LF line endings (not CRLF)
- Align assignments with spaces for readability (`=` aligned in blocks)
- Use `this.` prefix for instance members
- Use tabs for indentation
- Primary constructors for dependency injection
- Use `is null` / `is not null` instead of `== null`
- Make sure to comment only non-obvious code
- Separate code sections with spaces e.g. validation from logic
- Make `if(x) return y` sections same-line
- Break complex methods into smaller, focused functions (not too much tho, be reasonable)
- Add `[Trace("ClassName > MethodName")]` to async methods for observability (uses `Lis.Core.Util.TraceAttribute`)

### Commits
- Format: gitmoji + conventional commits (e.g. `✨ feat(agent): add prompt composer`)
- Always commit changes after completing each task
- Co-author: `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>`

### Tracing Conventions (OpenTelemetry)

Use `Activity.Current` to enrich traces with context:

```csharp
// Tags for key identifiers
Activity.Current?.SetTag("message.id", message.ExternalId);
Activity.Current?.SetTag("chat.id", message.ChatId);

// Status for errors
Activity.Current?.SetStatus(ActivityStatusCode.Error, errorMessage);
```

**Tag naming conventions:**
- `{entity}.id` - identifiers (e.g., `message.id`, `chat.id`)
- `{entity}.count` - counts (e.g., `messages.count`)
- `{operation}.status` - operation outcomes

### Database Conventions

- Entities use Data Annotations: `[Table("snake_case")]`, `[Column("snake_case")]`, `[JsonPropertyName]`
- Entity configurations use `IEntityTypeConfiguration<T>` in the same file as the entity
- Keep `LisDbContext.cs` clean - only `ApplyConfigurationsFromAssembly` in `OnModelCreating`
- Use `DateTimeOffset` for all timestamps (not `DateTime`)
- Add indexes on foreign keys and frequently queried columns
- `LisDbContextFactory` supports EF CLI migrations via `DotEnv.Load()`

### Service Conventions

- Interface + implementation in the **same .cs file** (except `IConversationService` and `IChannelClient` which live in Core)
- Register as `AddScoped<IService, Service>()` in Program.cs
- Add `[Trace]` attributes to all public async methods
- Use `Activity.Current?.SetTag()` for trace context enrichment
