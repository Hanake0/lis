# `/resume` Command + `session_id` on Messages

## Context

Once a user runs `/new`, the old conversation's context is gone. `/resume` brings it back — preferring **full session reopen** over a summary fallback. This also replaces the implicit `StartMessageId`/`EndMessageId` range-based message filtering with explicit `session_id` on messages (removing both fields entirely), and puts the existing unused `SummaryEmbedding` vectors to work for semantic search.

---

## Core Mechanism

### Full resume — reopen the target session

```
1. Finalize current session #50 (async summary generation)
2. Reopen target #42: clear Summary, SummaryEmbedding
3. Set chat.CurrentSessionId = 42
4. New messages get session_id = 42
5. Context loads WHERE m.SessionId == 42 — original + new messages
6. ParentSessionId chain intact — same parent summary injected
```

### Summary resume — fallback when tokens exceed budget

```
1. Finalize current session #50
2. Create new session with ParentSessionId = target.Id
3. ContextWindowBuilder injects target's summary as parent context
```

### When to fall back

Single check: **estimated tokens of target session's messages exceed `ResumeTokenBudget`**.

```csharp
int resumeBudget = lisOptions.ResumeTokenBudget > 0
    ? lisOptions.ResumeTokenBudget
    : (int)(modelSettings.ContextBudget * 0.7);

long estimatedTokens = await db.Messages
    .Where(m => m.SessionId == target.Id)
    .SumAsync(m => (long)(m.OutputTokens ?? m.InputTokens ?? 0), ct);
bool canFullResume = estimatedTokens <= resumeBudget;
```

`ResumeTokenBudget` defaults to 0 → uses 70% of `ContextBudget`. Set explicitly for fine-grained control.

Handles all end-reasons naturally:
- `/new` with small context → fits → full resume
- Manual `/compact` with small context → fits → full resume
- Auto-compaction (was at context limit) → exceeds → summary fallback

---

## UX

### `/resume` (no args) — list recent sessions
```
📋 Recent sessions:

#42 · 2h ago — Discussed flights, hotel in Lisbon, and Sintra day trip...
#38 · 1d ago — Brainstormed dinner recipes with chicken thighs and...
#35 · 2d ago — Reviewed Q1 roadmap and sprint planning. Decided to...

💡 /resume 42 or /resume trip to portugal
```

### `/resume 42` — full restore
```
🔄 Resumed session #42 with full context.
  📊 Context: ~8.2k/150k (5%) · Compaction at 120k
```

### `/resume 42` — summary fallback
```
⚠️ Session #42 context too large (~162k), resuming from summary.
📝 Discussed flights, hotel in Lisbon, and Sintra day trip...
```

### `/resume trip to portugal` — semantic search
```
🔍 Sessions matching "trip to portugal":

#42 · 2h ago — Discussed flights, hotel in Lisbon, and Sintra day trip...
#22 · 2w ago — Early trip brainstorming. Mentioned Porto and Algarve...

💡 /resume 42 to continue
```

### Edge cases
- Current session → `"You're already in this session."`
- Not found → `"Session #99 not found."`
- No sessions → `"No previous sessions to resume."`
- No search results → `"No sessions found matching "query"."`
- No summary on fallback → `"⚠️ No summary available — context may be limited."`

---

## Implementation

### Step 1 — Schema changes

**`Lis.Persistence/Entities/MessageEntity.cs`** — add `SessionId` (NOT NULL):

```csharp
[Column("session_id")]
[JsonPropertyName("session_id")]
public long SessionId { get; set; }

[ForeignKey(nameof(SessionId))]
public SessionEntity Session { get; set; } = null!;
```

Add index on `(SessionId, Timestamp)` in `MessageEntityConfiguration`.

**`Lis.Persistence/Entities/SessionEntity.cs`** — remove `StartMessageId` and `EndMessageId` entirely:

```csharp
// REMOVE:
// public long? StartMessageId { get; set; }
// public long? EndMessageId { get; set; }
```

No replacement field needed. A session is "active" when `chat.CurrentSessionId == session.Id`. No other finalization signal required.

**Migration** — drop DB and recreate (greenfield, breaking changes OK). Let EF generate the migration from the model changes. The migration will add `session_id` to message, drop `start_message_id`/`end_message_id` from session.

### Step 2 — Set `session_id` on all message persists

**`Lis.Agent/ConversationService.cs`**

Move session resolution to `IngestMessageAsync` (before `PersistMessageAsync`) so incoming messages get `session_id` immediately:

```csharp
public async Task<(ChatEntity Chat, bool ShouldRespond)> IngestMessageAsync(...) {
    ChatEntity chat = await UpsertChatAsync(db, message, ct);
    SessionEntity session = await EnsureSessionAsync(db, chat, ct);  // moved here
    await PersistMessageAsync(db, session, message, ct);  // now receives session
    // ...
}
```

`EnsureSessionAsync` simplified — no more `messageDbId` parameter (no `StartMessageId` to set).

`PersistMessageAsync` adds `SessionId = session.Id`.

`PersistSkMessageAsync` also adds `SessionId = session.Id` (receives session param).

Command response persist (line 95) adds `SessionId = session.Id`.

### Step 3 — Replace all `StartMessageId` queries with `session_id`

Every `WHERE (session.StartMessageId == null || m.Id >= session.StartMessageId)` becomes `WHERE m.SessionId == session.Id`:

- `ConversationService.RespondAsync` (line 111) — context building
- `ConversationService.CheckCompactionTriggersAsync` (lines 217, 234, 241)
- `CompactCommand.ExecuteAsync` (line 21)
- `StatusCommand.ExecuteAsync` (line 70)
- `CompactionService.CompactAsync` (line 50)
- `CompactionService.GenerateSessionSummaryAsync` (line 215)

Replace `session.EndMessageId is not null` check (line 184) with `chat.CurrentSessionId != session.Id` (reload chat to detect if compaction changed the active session).

Also remove `.Take(MaxRecentMessages)` (line 115) and `.OrderByDescending().OrderBy()` — just `.OrderBy(m => m.Timestamp)`.

### Step 4 — Compaction message reassignment

**`Lis.Agent/CompactionService.cs` — `CompactAsync`**

After creating the new session, reassign post-split messages within a transaction:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);

// Create new session, finalize old
// ... session.Summary, session.SummaryEmbedding, session.IsCompacting = false
SessionEntity newSession = new() { ChatId = chat.Id, ParentSessionId = session.Id, ... };
db.Sessions.Add(newSession);
chat.CurrentSessionId = newSession.Id;
await db.SaveChangesAsync(ct);

// Reassign kept messages to new session
await db.Database.ExecuteSqlInterpolatedAsync(
    $"UPDATE message SET session_id = {newSession.Id} WHERE session_id = {session.Id} AND id > {splitMessageId}", ct);

await transaction.CommitAsync(ct);
```

Transaction ensures atomicity: either both session creation AND message reassignment succeed, or neither does. Without the transaction, a failure after session creation but before reassignment would orphan messages in the old session while the AI loads from the new (empty) one.

**`StartNewSessionAsync`** (for `/new`) — remove `EndMessageId` logic. Session finalization is implicit: `chat.CurrentSessionId` points to the new session, so the old session is no longer active. No message reassignment needed for `/new` — all messages stay with their original session.

### Step 5 — Create `ResumeCommand`

**New: `Lis.Agent/Commands/ResumeCommand.cs`**

Constructor: `CompactionService`, `ModelSettings`, `IEmbeddingGenerator<string, Embedding<float>>?`

```csharp
public string[] Triggers => ["/resume"];

public async Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct) {
    if (ctx.Args is null or { Length: 0 })       → ListSessionsAsync
    if (long.TryParse(ctx.Args, out long id))    → ResumeByIdAsync
    else                                          → SearchSessionsAsync
}
```

**`ResumeByIdAsync`**:
```csharp
// 1. Token check
int resumeBudget = lisOptions.Value.ResumeTokenBudget > 0
    ? lisOptions.Value.ResumeTokenBudget
    : (int)(modelSettings.ContextBudget * 0.7);

long estimatedTokens = await db.Messages
    .Where(m => m.SessionId == target.Id)
    .SumAsync(m => (long)(m.OutputTokens ?? m.InputTokens ?? 0), ct);

if (estimatedTokens <= resumeBudget) {
    // Full resume: finalize current, reopen target
    if (ctx.Session is not null) {
        // fire async summary generation for old session
    }
    target.Summary          = null;
    target.SummaryEmbedding = null;
    ctx.Chat.CurrentSessionId = target.Id;
    await ctx.Db.SaveChangesAsync(ct);
    int pct = modelSettings.ContextBudget > 0
        ? (int)(estimatedTokens * 100 / modelSettings.ContextBudget) : 0;
    return $"🔄 Resumed session #{target.Id} with full context."
         + $"\n  📊 Context: ~{Fmt(estimatedTokens)}/{Fmt(modelSettings.ContextBudget)} ({pct}%)";
} else {
    // Summary fallback
    SessionEntity newSession = await compactionService.StartNewSessionAsync(
        ctx.Chat, ctx.Session, isExplicitBreak: true, ctx.Db, ct);
    newSession.ParentSessionId = target.Id;
    await ctx.Db.SaveChangesAsync(ct);
    return $"⚠️ Session #{target.Id} context too large (~{Fmt(estimatedTokens)}), resuming from summary."
         + $"\n📝 {Truncate(target.Summary, 120)}";
}
```

**`SearchSessionsAsync`** — embed query → `CosineDistance` on `SummaryEmbedding` (follows `MemoryPlugin` line 174). Fallback: `EF.Functions.ILike(s.Summary, $"%{query}%")`.

**`ListSessionsAsync`** — 5 most recent sessions that are not the current one (`s.Id != chat.CurrentSessionId`) and have summaries.

### Step 6 — Documentation

**Update: `docs/CONTEXT_COMPACTION.md`**

- Line 19: Remove `ParentSessionId = null` for `/new` — session finalization is now implicit via `chat.CurrentSessionId`
- Line 81: Add `/resume` to commands list
- Line 132: Update `SessionEntity.cs` description — remove StartMessageId/EndMessageId references
- Line 147: Replace "Load messages from current session (`StartMessageId` onwards)" with "Load messages from current session (`WHERE session_id = session.Id`)"
- Line 154: Update compaction step — mention message reassignment to new session
- Add `/resume` command to the commands section (line 81)

**New: `docs/SESSIONS.md`**

Document the session model and `/resume`:

- Session ownership: each message has `session_id` (NOT NULL FK to session)
- Active session: `chat.CurrentSessionId` is the single source of truth
- Session lifecycle: creation → messages accumulate → ended by `/new`, `/compact`, or auto-compaction
- Compaction: summarize + reassign post-split messages to new session (in transaction)
- `/resume` command: list, search, resume-by-ID
- Full resume vs summary fallback (token budget check)
- Semantic search on `SummaryEmbedding` (cosine distance)

### Step 7 — Register + cleanup

**`Lis.Agent/AgentSetup.cs`**: `services.AddSingleton<IChatCommand, ResumeCommand>();`

**Remove `MaxRecentMessages`** everywhere:
- `Lis.Core/Configuration/LisOptions.cs` — delete property
- `Lis.Agent/ConversationService.cs` — remove `.Take()`
- `Lis.Api/Program.cs` — remove env config line
- `Lis.Api/appsettings.json` — remove setting
- `.env.example` — remove `LIS_MAX_RECENT_MESSAGES=50`

**Add `ResumeTokenBudget`**:
- `Lis.Core/Configuration/LisOptions.cs` — add `public int ResumeTokenBudget { get; init; }` (default 0 → 70% of ContextBudget)
- `Lis.Api/Program.cs` — add `ResumeTokenBudget = EnvInt("LIS_RESUME_TOKEN_BUDGET", 0)`
- `.env.example` — add `LIS_RESUME_TOKEN_BUDGET=` (empty = 70% of context budget)

**Make `CompactionThreshold` percentage-based**:
- `Lis.Core/Configuration/LisOptions.cs` — change default to `0` (0 → 80% of ContextBudget)
- `Lis.Agent/ConversationService.cs` — resolve threshold: `lisOptions.Value.CompactionThreshold > 0 ? lisOptions.Value.CompactionThreshold : (int)(modelSettings.ContextBudget * 0.8)`
- `.env.example` — update `LIS_COMPACTION_THRESHOLD=` (empty = 80% of context budget)

---

## Files

| File | Action |
|------|--------|
| `Lis.Persistence/Entities/MessageEntity.cs` | **MODIFY** — add `SessionId` (NOT NULL, FK, index) |
| `Lis.Persistence/Entities/SessionEntity.cs` | **MODIFY** — remove `StartMessageId`/`EndMessageId` |
| `Lis.Persistence/Migrations/` | **NEW** — migration (backfill + schema change) |
| `Lis.Agent/ConversationService.cs` | **MODIFY** — session_id on persists, session_id queries, move session resolution to ingest, remove MaxRecentMessages |
| `Lis.Agent/CompactionService.cs` | **MODIFY** — session_id queries, message reassignment in transaction, remove StartMessageId/EndMessageId logic |
| `Lis.Agent/Commands/ResumeCommand.cs` | **NEW** — /resume command |
| `Lis.Agent/Commands/CompactCommand.cs` | **MODIFY** — session_id query |
| `Lis.Agent/Commands/StatusCommand.cs` | **MODIFY** — session_id query |
| `Lis.Agent/AgentSetup.cs` | **MODIFY** — register ResumeCommand |
| `Lis.Core/Configuration/LisOptions.cs` | **MODIFY** — remove MaxRecentMessages |
| `Lis.Api/Program.cs` | **MODIFY** — remove MaxRecentMessages config |
| `Lis.Api/appsettings.json` | **MODIFY** — remove MaxRecentMessages |
| `.env.example` | **MODIFY** — remove LIS_MAX_RECENT_MESSAGES |
| `docs/CONTEXT_COMPACTION.md` | **MODIFY** — update for session_id, add /resume, remove StartMessageId refs |
| `docs/SESSIONS.md` | **NEW** — session model, /resume, message ownership |

## Verify

- Drop DB, `dotnet ef database update` — migration applies clean
- `dotnet build` clean
- Messages persisted with correct `session_id`
- `/resume` → lists recent sessions
- `/resume <id>` on small session → full resume (reopen), Summary cleared, AI has actual messages
- `/resume <id>` on large session → summary fallback with warning
- `/resume <text>` → semantic search via cosine distance
- Compaction → kept messages reassigned atomically to new session
- `/status` uses session_id for message count
- No references to StartMessageId, EndMessageId, EndedAt, or MaxRecentMessages remain
