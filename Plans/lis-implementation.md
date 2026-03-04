# Lis — AI Personal Assistant Implementation Plan

## Context

Lis is a personal AI assistant inspired by OpenClaw, communicating via WhatsApp through go-whatsapp-web-multidevice (GOWA). The project is greenfield — .NET 10 scaffolding exists (Backend + Tests projects, Docker, Redis) but zero source code. The goal is reliability, usability, and token efficiency over feature breadth.

## OpenClaw Features → What We Copy vs Skip

### Copy (adapted for .NET + personal use)
| OpenClaw Feature | Lis Adaptation |
|---|---|
| **Browser automation** (CDP + snapshots) | Playwright MCP via Semantic Kernel's native MCP client |
| **Memory system** (JSONL + MEMORY.md + SQLite vectors) | PostgreSQL-backed memory with key-value facts + conversation summaries |
| **Heartbeat/proactive** (daemon wakes agent periodically) | `IHostedService` background timer for reminders + scheduled checks |
| **Skills** (SKILL.md files teaching agent tool use) | Semantic Kernel plugins (native, typed, testable) |
| **Context windowing** (compaction + pruning) | Token-budgeted context builder with rolling summarization |
| **Shell execution** (exec tool with approval) | Shell exec plugin (owner-only, no approval needed — personal device) |
| **Web search + fetch** | MCP or HTTP-based web tools |
| **Session persistence** (transcripts + memory) | All messages + summaries persisted in PostgreSQL |

### Skip (not relevant for single-user WhatsApp assistant)
- Multi-agent routing, multi-channel gateway, Canvas/A2UI, Voice Wake, Lobster Shell workflows, Control UI/WebChat, Chrome Extension relay mode, Plugin SDK, multi-tenant security

---

## Architecture

```
GOWA (WhatsApp) ──webhook──▸ Backend (ASP.NET Core, port 3010)
                                │
                         ┌──────┼──────────┐
                         ▼      ▼          ▼
                    WhatsApp  Conversation  Agent (Semantic Kernel)
                    Layer     Service        │
                         │      │      ┌────┼────┐
                         ▼      ▼      ▼    ▼    ▼
                       Redis   PostgreSQL  Plugins  MCP Servers
                       (cache) (persistence)        (Playwright browser)
```

**Message flow:** GOWA webhook POST → validate HMAC → persist message → build token-efficient context → invoke Semantic Kernel agent (with tools: memory, browser, datetime, reminders, shell, web) → persist response → send via GOWA REST API

---

## Folder Structure

```
Backend/
  Program.cs
  appsettings.json
  appsettings.Development.json

  Configuration/
    GowaOptions.cs            — GOWA connection (URL, device ID, webhook secret)
    AnthropicOptions.cs       — Anthropic API (bearer token, model ID, token limits)
    LisOptions.cs             — App settings (owner JID, language, thresholds)

  WhatsApp/
    GowaWebhookEndpoint.cs    — Minimal API POST /webhook/whatsapp
    WebhookValidator.cs       — HMAC-SHA256 signature check
    IGowaClient.cs            — Interface: send message, send presence, mark read
    GowaClient.cs             — Typed HttpClient with resilience pipeline
    Models/
      WebhookPayload.cs       — Incoming webhook deserialization
      SendMessageRequest.cs   — Outgoing message DTO

  Conversation/
    IConversationService.cs   — Orchestrator interface
    ConversationService.cs    — Full pipeline: receive → context → agent → respond
    ContextWindowBuilder.cs   — Token budget allocation and message selection
    ConversationSummarizer.cs — LLM-driven summarization of old messages

  Agent/
    AgentSetup.cs             — Kernel builder, plugin registration, MCP client setup
    SystemPrompt.cs           — Compact system prompt template (~150-200 tokens)
    Plugins/
      DateTimePlugin.cs       — Current date/time (pt-BR, BRT timezone)
      MemoryPlugin.cs         — Save/recall facts to PostgreSQL
      ReminderPlugin.cs       — Set/list/cancel timed reminders
      ShellPlugin.cs          — Execute shell commands on host
      WebFetchPlugin.cs       — Fetch and extract content from URLs

  Persistence/
    LisDbContext.cs           — EF Core DbContext
    Entities/
      ChatEntity.cs           — WhatsApp chat (JID, name, isGroup)
      MessageEntity.cs        — Message record (body, sender, tokenCount, timestamp)
      SummaryEntity.cs        — Conversation summary (text, message range, tokens)
      MemoryEntity.cs         — Long-term fact (key, value, source)
      ReminderEntity.cs       — Scheduled reminder (description, triggerAt, fired)

  Infrastructure/
    Telemetry/
      TelemetrySetup.cs       — OpenTelemetry traces + metrics + logs → Grafana
    Caching/
      ICacheService.cs        — Get/set/remove abstraction
      RedisCacheService.cs    — StackExchange.Redis implementation
    BackgroundServices/
      ReminderWorker.cs       — Polls reminders every 60s, sends via GOWA
      HeartbeatWorker.cs      — Periodic agent wake-up for proactive tasks

Tests/
  WhatsApp/
    WebhookValidatorTests.cs
    GowaWebhookEndpointTests.cs  — Integration tests via WebApplicationFactory
    GowaClientTests.cs
  Conversation/
    ContextWindowBuilderTests.cs
    ConversationSummarizerTests.cs
  Agent/
    MemoryPluginTests.cs
    ReminderPluginTests.cs
```

---

## Implementation Phases

### Phase 1 — Vertical Slice: Text In → Text Out

Get a working WhatsApp conversation end-to-end.

**Files to create:**

1. **Configuration POCOs** — `GowaOptions.cs`, `AnthropicOptions.cs`, `LisOptions.cs`
2. **WhatsApp layer** — `WebhookPayload.cs`, `SendMessageRequest.cs`, `WebhookValidator.cs`, `IGowaClient.cs`, `GowaClient.cs`, `GowaWebhookEndpoint.cs`
3. **Persistence** — `ChatEntity.cs`, `MessageEntity.cs`, `LisDbContext.cs`
4. **Agent** — `SystemPrompt.cs`, `DateTimePlugin.cs`, `AgentSetup.cs`
5. **Conversation** — `ContextWindowBuilder.cs`, `IConversationService.cs`, `ConversationService.cs`
6. **Entry point** — `Program.cs`, `appsettings.json`, `appsettings.Development.json`
7. **Docker** — Update `docker-compose.yml` (add PostgreSQL + GOWA containers)
8. **Tests** — `WebhookValidatorTests.cs`, `ContextWindowBuilderTests.cs`, `GowaWebhookEndpointTests.cs`

**Key design decisions:**
- Webhook returns 200 immediately, processes async (LLM calls take 2-10s)
- LLM: Claude (Anthropic API with bearer token auth via `x-api-key` header)
- Semantic Kernel configured with `AddAnthropicChatCompletion` (model: claude-sonnet-4-20250514 default, configurable)
- Token counting heuristic: `text.Length / 3.5` for Portuguese (no tokenizer dependency)
- Context budget: system prompt (~200t) + summary (~400t) + recent messages (fill remaining) + response reserve
- Resilience: retry 3x exponential backoff + circuit breaker + 15s timeout on GOWA calls

### Phase 2 — Memory + Summarization

Long conversations stay efficient. Important facts persist.

**Files to create:**

1. `SummaryEntity.cs`, `MemoryEntity.cs` + update `LisDbContext.cs` + EF migration
2. `ConversationSummarizer.cs` — triggers when unsummarized messages > threshold (default 30), runs async after response
3. `MemoryPlugin.cs` — save_memory / recall_memory kernel functions
4. Update `ContextWindowBuilder.cs` to include summaries + memories in context
5. Update `ConversationService.cs` to integrate both
6. Tests: `ConversationSummarizerTests.cs`, `MemoryPluginTests.cs`

**Summarization strategy (inspired by OpenClaw compaction):**
- Rolling summaries: each new summary incorporates the previous one
- Summary prompt: "Summarize in 2-3 sentences, preserving key facts, decisions, and action items"
- Runs in background after response sent (no latency impact)
- Cached in Redis for fast access

### Phase 3 — Browser Automation (MCP)

Lis can browse the web programmatically.

**Files to create/modify:**

1. Add NuGet package: `ModelContextProtocol`
2. Update `AgentSetup.cs` — spawn Playwright MCP server as child process, connect via STDIO, register tools as Semantic Kernel plugin
3. Add `playwright-mcp` Docker container to `docker-compose.yml` (Node.js + @playwright/mcp)
4. Add `McpOptions.cs` for MCP server configuration
5. `WebFetchPlugin.cs` — simple HTTP fetch + HTML-to-text for lightweight web reads (no browser needed)

**How it works:**
- On startup, `AgentSetup.cs` creates an MCP client connected to the Playwright MCP server
- MCP tools (navigate, click, type, screenshot, extract_text, etc.) are discovered and wrapped as Semantic Kernel functions
- The agent can invoke them during reasoning via auto function calling
- Browser runs headless in a Docker container

**Docker setup:**
```yaml
playwright-mcp:
  image: node:22-alpine
  command: ["npx", "-y", "@playwright/mcp@latest", "--headless"]
  restart: unless-stopped
```

### Phase 4 — Reliability + Observability

Production-ready infrastructure.

1. `TelemetrySetup.cs` — OpenTelemetry config (traces, metrics, logs → OTLP exporter)
2. `ICacheService.cs` + `RedisCacheService.cs` — Redis wrapper for summary caching + conversation locking
3. Health check endpoint: `/health` (DB + Redis + GOWA connectivity)
4. Structured logging throughout with `ILogger<T>`
5. Conversation lock via Redis (prevent double-processing same message)

### Phase 5 — Proactive Features

Lis reaches out to you, not just responds.

1. `ReminderEntity.cs` + update `LisDbContext.cs` + migration
2. `ReminderPlugin.cs` — set/list/cancel reminders via agent tools
3. `ReminderWorker.cs` — `BackgroundService` polling every 60s, sends due reminders via GOWA
4. `HeartbeatWorker.cs` — periodic agent wake-up (configurable interval), checks pending tasks
5. `ShellPlugin.cs` — execute shell commands on the host machine

---

## docker-compose.yml (Final)

```yaml
services:
  backend:
    build:
      context: .
      dockerfile: ./Backend/Dockerfile
    container_name: backend
    restart: unless-stopped
    env_file: .env
    ports:
      - "3010:3010"
    depends_on:
      - backend-redis
      - postgres
      - gowa

  backend-redis:
    container_name: backend-redis
    image: redis:8-alpine
    restart: always

  postgres:
    container_name: backend-postgres
    image: postgres:17-alpine
    restart: always
    environment:
      POSTGRES_DB: lis
      POSTGRES_USER: lis
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data

  gowa:
    container_name: gowa
    image: aldinokemal/go-whatsapp-web-multidevice:latest
    restart: unless-stopped
    ports:
      - "3000:3000"
    environment:
      APP_PORT: "3000"
      WHATSAPP_WEBHOOK: "http://backend:3010/webhook/whatsapp"
      WHATSAPP_WEBHOOK_SECRET: "${GOWA_WEBHOOK_SECRET}"
      WHATSAPP_WEBHOOK_EVENTS: "message"

  playwright-mcp:
    container_name: playwright-mcp
    image: node:22-alpine
    command: ["npx", "-y", "@playwright/mcp@latest", "--headless"]
    restart: unless-stopped

volumes:
  pgdata:
```

---

## Token Efficiency Strategy

Inspired by OpenClaw's compaction/pruning but simplified:

| Layer | Strategy | Token Cost |
|---|---|---|
| System prompt | Minimal, ~150-200 tokens. No verbose persona. | Fixed ~200 |
| Tool schemas | Claude tool_use with auto function calling | ~300-500 |
| Conversation summary | Rolling summaries replace old messages | ~200-500 |
| Memory facts | Include only relevant facts (key prefix match) | ~100-200 |
| Recent messages | Fill remaining budget, newest first | Variable |
| Response reserve | `MaxTokens` setting (default 4096) | Reserved |

**Total budget example (12K context):** 200 (system) + 400 (tools) + 400 (summary) + 150 (memory) + 4096 (response reserve) = ~6,750 for recent messages ≈ 30-50 WhatsApp messages.

---

## Verification Plan

1. **Unit tests** — `dotnet test` (webhook validation, context windowing, plugin logic)
2. **Integration test** — `WebApplicationFactory` sends mock webhook, verifies full pipeline
3. **Manual E2E** — `docker compose up`, scan QR in GOWA UI (localhost:3000), send WhatsApp message, verify response
4. **Browser test** — Ask Lis "what's on the front page of hacker news?" and verify it uses browser tools
5. **Memory test** — Tell Lis a fact, start new conversation, ask about it
6. **Summarization test** — Send 30+ messages in a conversation, verify summary is generated and context stays within budget
