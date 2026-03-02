# Context Compaction & Sessions

## Overview

Lis uses **rolling async context compaction** to manage long conversations. When context
grows too large, older messages are summarized into a compact block while the conversation
continues uninterrupted. Each compaction creates a new **session**, preserving the summary
and an embedding for future semantic search.

## Design Decisions

### Sessions — not merged summaries

Each session is a self-contained segment of a chat with its own summary. Full compaction
always creates a new session. Summaries are never merged into each other.

- **Compaction** → finalize current session (summary + embedding) → create new session
  with `ParentSessionId = old.Id` (continuation chain).
- **`/new` or `/clear`** → finalize session → create new session with
  `ParentSessionId = null` (explicit break).
- **Context for continuations**: the parent session's summary is injected when building
  context, giving the AI continuity without merging.

### No token estimation — ever

Token counts come exclusively from provider response metadata (`input_tokens`,
`output_tokens`, `cache_read_input_tokens`, `cache_creation_input_tokens`).
The old `EstimateTokens` heuristic (`chars / 3.5`) is removed entirely.

- **Compaction trigger**: based on actual `input_tokens` from the last response.
- **Pre-send check**: for very large contexts, `CountMessageTokensAsync`
  (Anthropic's free endpoint) can validate before sending.
- **Per-message storage**: actual token counts stored as columns on `MessageEntity`,
  not buried in `SkContent` JSON.

### Progressive compaction — tool pruning first

Two stages, applied in order:

1. **Tool output pruning** — triggered when total tool result tokens (after
   `ToolsPrunedThroughId`) exceed `ToolPruneThreshold`. Replaces tool output bodies
   with one-liners like `[result: get_current_datetime]`. Non-destructive (DB unchanged).
   Runs ONCE, stays stable → preserves prompt cache.

2. **Full compaction** — triggered when `input_tokens` from the last response exceeds
   `CompactionThreshold`. Keeps `KeepRecentTokens` of recent messages. Everything
   before that is summarized by the compaction LLM. Creates a new session.

### Tool summarization policy

Global env `LIS_TOOL_SUMMARIZATION_POLICY`:
- `auto` — per-tool `[ToolSummarization]` attribute. Default: Prune.
- `keep_all` — never prune. Safeguard: force-prunes if context blows past threshold.
- `keep_none` — always prune everything.

Per-tool attribute `SummarizationPolicy`:
- `Prune` — replace with one-liner.
- `Summarize` — include full output in compaction prompt for LLM summarization
  (e.g., book content with emotional value).

### Prompt caching — 4 breakpoints

Anthropic's prompt caching (up to 4 `cache_control` breakpoints):

1. After system prompt — stable, rarely changes.
2. After session summary (if exists) — changes only on compaction.
3. At tool prune boundary — ensures pruning doesn't invalidate later messages' cache.
4. Top-level automatic — `cache_control` on request body, auto-caches growing conversation.

Implemented via `CacheControlHandler` DelegatingHandler that injects `cache_control`
markers into the HTTP request JSON.

### Compaction client — separate provider

Compaction can use a different provider/model than the main conversation (e.g., Haiku
for cheap summaries). Registered as keyed `IChatClient("compaction")`. Falls back to
main client if not configured.

### Chat commands — intercepted before AI

`/status`, `/new`, `/clear` are handled by `CommandRouter` before AI processing.
No AI tokens wasted on commands.

### Thinking effort — configurable

`ANTHROPIC_THINKING_EFFORT` env var: `low` (1024), `medium` (4096), `high` (16384),
or exact token count.

## Configuration Reference

| Env Var | Default | Purpose |
|---------|---------|---------|
| `LIS_KEEP_RECENT_TOKENS` | 4000 | Recent messages kept verbatim after compaction |
| `LIS_TOOL_PRUNE_THRESHOLD` | 8000 | Tool output tokens to trigger pruning |
| `LIS_TOOL_KEEP_THRESHOLD` | 2000 | Recent tool output tokens to keep unpruned |
| `LIS_COMPACTION_THRESHOLD` | 10000 | Input tokens to trigger full compaction |
| `LIS_COMPACTION_NOTIFY` | true | Notify user on compaction events |
| `LIS_TOOL_SUMMARIZATION_POLICY` | auto | `auto`, `keep_all`, `keep_none` |
| `LIS_COMPACTION_PROVIDER` | *(main)* | `anthropic`, `openai` |
| `LIS_COMPACTION_API_KEY` | | API key for compaction provider |
| `LIS_COMPACTION_MODEL` | | Model for summarization |
| `ANTHROPIC_CACHE_TTL` | 5m | `5m` or `1h` |
| `ANTHROPIC_CACHE_ENABLED` | true | Toggle prompt caching |
| `ANTHROPIC_THINKING_EFFORT` | *(off)* | Thinking budget |

## Notification Formats

Tool pruning:
```
🔧 Tool outputs pruned (12.4k → 2.1k, -83%)
  📊 Context: 18.5k/150k (12%)
```

Full compaction:
```
⚙️ Compacted (157k → 9.1k)
  🔧 System: 1.8k tokens
  📝 Summary: 3.2k tokens
  💬 Kept context: 4.8k tokens
  🛠️ Tools: 1.1k tokens
  📊 Total: 9.1k/150k (6%)
```

## Implementation

### Key files

| File | Purpose |
|------|---------|
| `Lis.Persistence/Entities/SessionEntity.cs` | Session data model with summary, embedding, token stats |
| `Lis.Agent/CompactionService.cs` | Async summarization, session lifecycle, embedding generation |
| `Lis.Agent/ContextWindowBuilder.cs` | History assembly with session/summary injection, tool pruning |
| `Lis.Agent/ConversationService.cs` | Session management, compaction triggers, command routing |
| `Lis.Agent/ToolRunner.cs` | Token usage extraction from streaming responses |
| `Lis.Agent/Commands/` | Command framework: `IChatCommand`, `CommandRouter`, `/status`, `/new` |
| `Lis.Providers/Anthropic/AnthropicProvider.cs` | CacheControlHandler, thinking effort, cache config |
| `Lis.Core/Util/ToolSummarizationAttribute.cs` | Per-tool summarization policy attribute |
| `Lis.Core/Channel/TokenUsage.cs` | Token usage DTO |

### Flow

1. Message arrives → `ConversationService.RespondAsync`
2. Ensure session exists (auto-create on first message)
3. Check commands (`/status`, `/new`, `/clear`) → handle without AI
4. Load messages from current session (`StartMessageId` onwards)
5. Build context: system prompt → parent summary → session summary → messages (with pruning)
6. Send to AI via ToolRunner (streaming, with tool loop)
7. Extract `TokenUsage` from response metadata → update session stats + message columns
8. Check compaction triggers (based on actual `input_tokens`):
   - Tool prune threshold → set `ToolsPrunedThroughId`
   - Compaction threshold → fire async `CompactionService.CompactAsync`
9. Compaction: summarize → embed → finalize session → create new session

### Prompt caching

`CacheControlHandler` DelegatingHandler intercepts Anthropic API requests and injects:
- Top-level `cache_control` for automatic caching
- Explicit `cache_control` on last system content block

The handler is inserted in the HttpClient pipeline before `BearerAuthHandler`.
Cache stats (`cache_read_input_tokens`, `cache_creation_input_tokens`) are extracted
from response metadata and stored per-message and per-session.
