# Lis

A production-grade AI assistant that lives where you already chat. Not a wrapper around an API — a full agent with tools, memory, security, cost control, and group intelligence.

## Why Lis?

Projects like [OpenClaw](https://github.com/nichochar/openclaw) offer impressive breadth — 25 channels, 15+ providers, mobile apps. But they lack depth where it matters for daily use: no prompt caching (every turn pays full token cost), no per-message cost tracking, no runtime config editing, no crash-safe message queue, no session resume by topic, and memory stored in markdown files instead of a queryable database.

Lis takes the opposite approach. It goes deep on the things that make an AI assistant actually usable as a daily driver:

**Cost as architecture, not afterthought.** Four prompt cache breakpoints keep Anthropic cache hit rates high — the difference between $3/day and $0.30/day. Two-stage context compaction uses a cheap model (Haiku) for summarization while the main model handles conversation. Per-message token tracking means you always know exactly what you're spending. Tool output pruning silently strips verbose results before they bloat your context window.

**Mistrust by design.** Five independent security layers gate every interaction: who can talk, what tools are visible, who can invoke them, whether shell commands need human approval, and whether file paths stay in the sandbox. Non-owners can see sensitive tools but can't execute them. Approval flows use WhatsApp reactions (👍 once, ✅ always, ❌ deny) for a native UX.

**Built for real messaging.** Message debouncing batches rapid messages. A crash-safe queue persists unprocessed messages in PostgreSQL. Typing indicators show the AI is thinking. Clock reactions mark queued messages. The AI sends incremental progress during multi-tool tasks instead of making you wait for everything to finish. Runtime config editing means you tune the AI mid-conversation — no restarts, no config files.

### Lis vs OpenClaw

| Capability | Lis | OpenClaw |
|-----------|-----|----------|
| **Prompt caching** | 4 cache breakpoints, real-time hit % tracking | None — every turn pays full token cost |
| **Context compaction** | Two-stage (tool pruning + summary) with separate cheap model | Single-stage summary, no separate model |
| **Per-message cost tracking** | Input, output, cache read, cache creation per message | No per-message tracking |
| **Tool summarization policies** | Per-tool `Prune` / `Summarize` control | No per-tool policy |
| **Session resume** | `/resume` by ID or semantic search (pgvector) | No equivalent |
| **Memory storage** | PostgreSQL + pgvector with CRUD tools | Markdown files (not queryable) |
| **Runtime config editing** | Change any setting mid-conversation via tools | Requires config file edit + restart |
| **Prompt section editing** | AI can view and modify its own system prompt at runtime | No runtime prompt editing |
| **Agent switching per chat** | `/agent coding` to switch tools mid-conversation | Agents routed at gateway level, not per-turn |
| **Message queue** | Crash-safe, persisted in PostgreSQL | In-memory only, lost on crash |
| **Message debounce UX** | 🕐 reaction on queued messages, typing resets timer | Silent internal queue, no user feedback |
| **Response directives** | `[QUOTE]`, `NO_RESPONSE`, react by message ID | No equivalent |
| **@mention resolution** | Bidirectional phone↔name via DB | One-way |
| **Channels** | 1 (WhatsApp) — channel-agnostic architecture | 25 channels |
| **AI providers** | 1 (Anthropic) — provider-agnostic architecture | 15+ providers |
| **Mobile/desktop apps** | None | iOS, Android, macOS |
| **Cron/automation** | None | Built-in cron + webhooks |
| **Subagent spawning** | None | Task decomposition via child agents |

Lis wins on **depth** — cost optimization, context management, runtime flexibility, and crash safety. OpenClaw wins on **breadth** — more channels, providers, and platform integrations.

## Features

### Agentic Tool Loop

The AI doesn't just generate text. It operates in a **streaming multi-turn tool loop** — calling tools, receiving results, reasoning about them, and responding — up to 10 iterations per turn. Each assistant message is sent to the user as it's produced, so you see incremental progress during complex tasks instead of waiting for a single monolithic response.

Every tool call sends a real-time notification to the chat ("🔍 Searching...", "📄 Reading file...", "🖥️ Running: git status") so you always know what the AI is doing. These notifications are configurable per-agent — turn them off for a cleaner experience, or keep them for full visibility.

### Cost & Context Optimization

Running an AI agent on a messaging platform means long-lived conversations with heavy tool use. Without active cost management, token usage explodes.

- **Two-stage context compaction** — (1) tool output pruning silently strips verbose results to bare function names when they exceed a threshold, (2) full compaction summarizes older context while keeping recent messages verbatim. Both trigger automatically based on configurable token budgets.
- **4 prompt cache breakpoints** — cache markers at system prompt, session summary, tool prune boundary, and top-level maximize Anthropic prompt cache hits. The difference between 0% and 90% cache hits is the difference between $3/day and $0.30/day.
- **Separate compaction model** — use Haiku for summarization while keeping Sonnet or Opus for conversation. Compaction cost drops by 10x.
- **Tool summarization policies** — per-tool control: `Prune` strips verbose output (file listings, search results), `Summarize` preserves valuable context (memory searches, config reads) in compaction summaries.
- **Per-message token tracking** — input, output, cache read, cache creation — stored per-message and per-session. `/status` shows real-time cache hit %, token usage, and budget utilization.

### Tools (33 functions across 9 plugins)

| Plugin | Functions | What it does |
|--------|-----------|-------------|
| **Exec** | `exec_run_command` | Shell execution (bash/cmd) with timeout, output cap, and workspace sandbox |
| **FileSystem** | `fs_read_file`, `fs_write_file`, `fs_edit_file`, `fs_list_directory`, `fs_search_files` | File operations with symlink-aware path validation |
| **Browser** | `browser_start`, `browser_navigate`, `browser_snapshot`, `browser_screenshot`, `browser_click`, `browser_type`, `browser_evaluate`, `browser_tabs`, `browser_close` | Headless Chromium automation via Playwright |
| **Web** | `web_search`, `web_fetch` | Brave Search API + URL fetch with HTML stripping |
| **Memory** | `mem_create_memory`, `mem_search_memories`, `mem_update_memory`, `mem_delete_memory` | Semantic memory with pgvector + full-text search fallback |
| **Prompt** | `prompt_list_prompt_sections`, `prompt_get_prompt_section`, `prompt_update_prompt_section` | Runtime-editable system prompt sections — the AI can modify its own instructions |
| **Config** | `cfg_get_agent_config`, `cfg_update_agent_config`, `cfg_get_chat_config`, `cfg_update_chat_config`, `cfg_add_allowed_sender`, `cfg_remove_allowed_sender`, `cfg_list_allowed_senders`, `cfg_list_chats` | Dynamic per-chat and per-agent configuration without restarts |
| **Response** | `resp_react_to_message` | React to messages with emoji by ID |
| **DateTime** | `dt_get_current_datetime` | Current date/time in agent timezone |

### Multi-Agent

Not every conversation needs the same model, tools, or personality.

- **Named agents** with independent model, prompts, tool profiles, and compaction settings.
- **Switch agents per chat** — `/agent coding` for development tasks, `/agent default` for casual conversation. No restart, no config file edits.
- **Tool profiles** — `minimal` (read-only) → `standard` (general) → `coding` (exec + files) → `full` (everything including browser). Plus per-agent allow/deny glob patterns for fine-grained control.
- **Create and delete agents** — `/agent new research "Research Assistant"`, `/agent delete research`.
- **Runtime config editing** — change any setting mid-conversation via tools. The AI can tune its own parameters.

### Security (5-layer defense-in-depth)

Mistrust by design. Each layer operates independently — compromising one doesn't bypass the others.

| Layer | Gate | What it does |
|-------|------|-------------|
| **1. ShouldRespond** | Chat-level | Enabled flag, authorized senders list, mention requirements, owner bypass |
| **2. Tool Policy** | Context visibility | Profile filter + allow/deny globs control which tools the AI can even see |
| **3. Tool Auth** | Per-tool invocation | `Open` / `OwnerOnly` / `ApprovalRequired` — checked at runtime, not just hidden |
| **4. Exec Approval** | Human gating | Allowlist patterns + async approval via reactions (👍 once, ✅ always + add to allowlist, ❌ deny) |
| **5. Workspace Sandbox** | Path boundary | Symlink-aware path validation — resolves symlink targets to prevent filesystem escape |

### Sessions & Memory

Conversations don't end when the context window fills up.

- **Linked-list sessions** — compaction creates a child session with a summary. Sessions form a navigable history, not a flat log.
- **`/resume`** — revisit any past session. If it fits the budget, load full original messages. If not, inject the summary as context. Search by session ID or by topic (pgvector semantic search on summaries).
- **Semantic memory** — explicit CRUD tools backed by pgvector embeddings with full-text search fallback. The AI creates, searches, updates, and deletes memories through natural conversation.
- **Crash-safe queue** — messages received during AI processing are persisted in PostgreSQL. On restart, they're flushed and processed. No messages lost.

### UX That Feels Native

The goal is an experience that feels like messaging a person, not operating a bot.

- **Typing indicator** — WhatsApp "typing..." shown while the AI is processing.
- **Clock reaction** — when the AI is busy and you send another message, it gets a 🕐 reaction so you know it was received. Removed after processing.
- **Message debouncing** — rapid messages are batched with a configurable delay (default 3s, per-chat override). Typing events from the user reset the timer, so the AI waits until you're done.
- **Response directives** — the AI chooses how to respond: `[QUOTE]` to quote the user's message, `NO_RESPONSE` to suppress text after a reaction, or react to specific messages by ID.
- **`/abort`** — cancel in-progress AI responses instantly. The AI stops generating, queued messages are flushed, and the chat is ready for new input.
- **Markdown → WhatsApp** — headers, bold, italic, code blocks, lists, and links automatically converted to WhatsApp's native formatting.

### Group Intelligence

Groups aren't second-class citizens. They have dedicated logic for authorization, context, and interaction.

- **3-layer mention detection** — native WhatsApp @mention, reply-to-bot (implicit mention), and configurable text triggers (comma-separated per agent, e.g., `"lis,liszinha"`).
- **Bidirectional @mention resolution** — incoming `@552731911808` → `@Alice` (the AI sees names, not numbers), outgoing `@Alice` → `@552731911808` (creates native WhatsApp @mentions). Resolved via the messages database with duplicate name disambiguation.
- **Context windowing** — keep all bot messages + last N consecutive user messages (default 5, per-chat override). Reduces noise without losing relevant context.
- **Group metadata injection** — group name and topic fetched from the WhatsApp API, injected into the system prompt so the AI knows where it is.

### Media

- **Image/sticker understanding** — sent to Claude as multimodal content. The AI sees what you send.
- **Audio transcription** — OpenAI Whisper integration. Audio messages are transcribed and injected inline as `<Audio transcript: ...>`.
- **Auto-download** — media downloaded from WhatsApp CDN automatically.

## Architecture

```
WhatsApp ←→ GOWA (Go) ←→ Webhook ←→ Lis.Api (ASP.NET Core)
                                          ↓
                                    MessageDebouncer ─── debounce + queue
                                          ↓
                                  ConversationService ── context, compaction, sessions
                                          ↓
                                      ToolRunner ─────── streaming agentic loop
                                       ↙     ↘
                                 Lis.Tools   AI Provider (Anthropic)
                                    ↓
                               PostgreSQL ── messages, sessions, memory, agents
```

### Project Structure

```
Lis.Core          — Interfaces, configuration, utilities (no heavy deps)
Lis.Persistence   — EF Core DbContext, entities, migrations (PostgreSQL)
Lis.Agent         — Conversation orchestration, context window, compaction, sessions
Lis.Tools         — Semantic Kernel plugins (exec, fs, browser, web, memory, ...)
Lis.Providers     — AI provider implementations (Anthropic, extensible)
Lis.Channels      — Messaging channels (WhatsApp/GOWA, extensible)
Lis.Api           — ASP.NET Core host, composition root, Dockerfile
Lis.Tests         — xUnit test suite
```

**Provider-agnostic**: swap AI providers by implementing `IChatClient`. **Channel-agnostic**: add messaging platforms by implementing `IChannelClient`. The conversation engine knows nothing about WhatsApp or Anthropic.

## Quick Start

### Prerequisites
- Docker & Docker Compose
- Anthropic API key ([console.anthropic.com](https://console.anthropic.com))

### Setup

```bash
git clone <repo-url> && cd lis
cp .env.example .env
# Edit .env — set at minimum:
#   ANTHROPIC_API_KEY=sk-ant-...
#   LIS_OWNER_JID=<your-phone>@s.whatsapp.net
#   GOWA_WEBHOOK_SECRET=<random-string>

docker compose up -d
# Scan QR code at http://localhost:3000
# Message your bot on WhatsApp
```

### Services

| Service | Port | Description |
|---------|------|-------------|
| `backend` | 3010 | Lis API |
| `gowa` | 3000 | WhatsApp bridge |
| `postgres` | 5432 | PostgreSQL |
| `backend-redis` | 6379 | Redis |

## Development

```bash
# Run locally (requires PostgreSQL running)
cd Lis.Api && dotnet run

# Build
dotnet build

# Run tests
dotnet test Lis.Tests/Lis.Tests.csproj

# Run migrations
dotnet ef database update \
  --project Lis.Persistence/Lis.Persistence.csproj \
  --startup-project Lis.Api/Lis.Api.csproj

# Code cleanup (ReSharper)
jb cleanupcode Lis.Api/Lis.Api.csproj \
  --profile="Built-in: Full Cleanup" \
  --settings=Lis.sln.DotSettings
```

## Configuration

All settings are environment variables. See [.env.example](.env.example) for the full list.

### AI Provider

```env
ANTHROPIC_ENABLED=true
ANTHROPIC_API_KEY=sk-ant-...
ANTHROPIC_MODEL=claude-sonnet-4-20250514
ANTHROPIC_MAX_TOKENS=4096
ANTHROPIC_CONTEXT_BUDGET=128000
ANTHROPIC_THINKING_EFFORT=medium          # off, low, medium, high, or token count
ANTHROPIC_CACHE_ENABLED=true
```

### Channel (WhatsApp)

```env
GOWA_ENABLED=true
GOWA_BASE_URL=http://gowa:3000
GOWA_WEBHOOK_SECRET=your-secret
LIS_OWNER_JID=5511999999999@s.whatsapp.net
```

### Context & Cost

```env
LIS_KEEP_RECENT_TOKENS=4000              # tokens kept verbatim after compaction
LIS_TOOL_PRUNE_THRESHOLD=8000            # trigger tool output pruning
LIS_COMPACTION_THRESHOLD=80              # % of budget to trigger full compaction
LIS_COMPACTION_MODEL=claude-haiku-4-5-20251001  # cheap model for summaries
```

### Tools

```env
LIS_TOOL_NOTIFICATIONS=true
LIS_MAX_TOOL_ITERATIONS=10
LIS_WEB_SEARCH_ENABLED=true
LIS_WEB_SEARCH_API_KEY=BSA...            # Brave Search API
OPENAI_API_KEY=sk-...                    # enables Whisper transcription
```

## Commands

| Command | Description |
|---------|-------------|
| `/status` | Show agent, model, tokens, cache stats, session info |
| `/agent [name]` | Show or switch current agent |
| `/agents` | List all agents |
| `/agent new <name> [display]` | Create a new agent |
| `/model [name]` | Show or change model |
| `/models` | List known models |
| `/new`, `/clear` | Start a new session |
| `/resume [id\|text]` | Resume a previous session (by ID or semantic search) |
| `/compact` | Manually trigger context compaction |
| `/prune` | Manually prune tool outputs |
| `/abort`, `/stop`, `/cancel` | Cancel in-progress AI response |
| `/approve <id>`, `/deny <id>` | Resolve exec approval requests |

## Documentation

| Doc | Topic |
|-----|-------|
| [AGENTS.md](docs/AGENTS.md) | Multi-agent system, per-chat config, agent switching |
| [CONTEXT_COMPACTION.md](docs/CONTEXT_COMPACTION.md) | Rolling compaction, sessions, token tracking, prompt caching |
| [SECURITY_MODEL.md](docs/SECURITY_MODEL.md) | 5-layer defense, tool auth, workspace sandbox |
| [TOOLS.md](docs/TOOLS.md) | Plugin architecture, tool profiles, authorization levels |
| [TOOL_POLICY.md](docs/TOOL_POLICY.md) | Tool availability profiles and allow/deny globs |
| [GROUPS.md](docs/GROUPS.md) | Group authorization, mention detection, context windowing |
| [SESSIONS.md](docs/SESSIONS.md) | Session lifecycle, `/resume`, semantic search |
| [MEDIA_HANDLING.md](docs/MEDIA_HANDLING.md) | Image vision, audio transcription, media download |
| [BROWSER.md](docs/BROWSER.md) | Headless Chromium automation via Playwright |
| [EXEC_APPROVALS.md](docs/EXEC_APPROVALS.md) | Shell command approval workflow |
| [MESSAGE_QUEUE.md](docs/MESSAGE_QUEUE.md) | Crash-safe queuing, `/abort`, flush loop |
| [MESSAGE_FORMATTING.md](docs/MESSAGE_FORMATTING.md) | Markdown → WhatsApp conversion |
| [RESPONSE_DIRECTIVES.md](docs/RESPONSE_DIRECTIVES.md) | `[QUOTE]`, `NO_RESPONSE`, reactions |
| [debounce.md](docs/debounce.md) | Message batching and typing reset |

## Tech Stack

- **Runtime**: .NET 10 / ASP.NET Core
- **AI**: Microsoft Semantic Kernel + Anthropic SDK
- **Database**: PostgreSQL + EF Core + pgvector
- **WhatsApp**: [GOWA](https://github.com/aldinokemal/go-whatsapp-web-multidevice) (Go WhatsApp Web)
- **Browser**: Playwright (Chromium)
- **Search**: Brave Search API
- **Transcription**: OpenAI Whisper
- **Deployment**: Docker Compose

## License

MIT
