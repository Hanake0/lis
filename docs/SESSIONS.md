# Sessions & `/resume`

## Session Model

Each chat has an **active session** identified by `chat.CurrentSessionId`. Sessions form
a linked list via `ParentSessionId` — compaction creates child sessions, `/new` creates
unlinked sessions (`ParentSessionId = null`).

### Message ownership

Every message has a NOT NULL `session_id` FK to session. Context is built with
`WHERE m.SessionId == session.Id` — no range-based filtering.

### Session lifecycle

```
Created → messages accumulate → ended by:
  /new or /clear  → async summary generated, new session created (no parent)
  /compact        → async compaction, kept messages reassigned to new session
  auto-compaction → same as /compact but triggered by threshold
  /resume         → old session reopened (full) or new session with parent = target (summary)
```

A session is "active" when `chat.CurrentSessionId == session.Id`. The active session is
determined solely by `CurrentSessionId`.

### Compaction message reassignment

During compaction, messages after the split point must move to the new session. This is
done atomically in a database transaction:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);

// Finalize old session, create new session
session.Summary = summary;
session.SummaryEmbedding = embedding;
session.IsCompacting = false;
SessionEntity newSession = new() { ChatId = chat.Id, ParentSessionId = session.Id };
db.Sessions.Add(newSession);
chat.CurrentSessionId = newSession.Id;
await db.SaveChangesAsync(ct);

// Reassign kept messages
await db.Database.ExecuteSqlInterpolatedAsync(
    $"UPDATE message SET session_id = {newSession.Id} WHERE session_id = {session.Id} AND id > {splitMessageId}", ct);

await transaction.CommitAsync(ct);
```

Without the transaction, a failure after session creation but before reassignment would
orphan messages in the old session while the AI loads from the new (empty) one.

## `/resume` Command

### Modes

| Usage | Behavior |
|-------|----------|
| `/resume` | List 5 most recent non-current sessions with summaries |
| `/resume <id>` | Resume session by ID (full or summary fallback) |
| `/resume <text>` | Semantic search on session summaries |

### Full resume

When the target session's estimated token count fits within `ResumeTokenBudget`
(default: 70% of `ContextBudget`):

1. Generate async summary for the current session
2. Clear the target session's `Summary` and `SummaryEmbedding` (they'd be redundant
   with the actual messages now loaded)
3. Set `chat.CurrentSessionId = target.Id`
4. New messages get `session_id = target.Id`
5. AI sees original messages + new ones

```
🔄 Resumed session #42 with full context.
  📊 Context: ~8.2k/150k (5%) · Compaction at 120k
```

### Summary fallback

When estimated tokens exceed `ResumeTokenBudget`:

1. Generate async summary for the current session
2. Create new session with `ParentSessionId = target.Id`
3. `ContextWindowBuilder` injects target's summary as parent context

```
⚠️ Session #42 context too large (~162k), resuming from summary.
📝 Discussed flights, hotel in Lisbon, and Sintra day trip...
```

### Semantic search

When `/resume <text>` is used:

- If `IEmbeddingGenerator` is available: embed the query → `CosineDistance` on
  `SummaryEmbedding` (pgvector HNSW index with `vector_cosine_ops`)
- Fallback: `EF.Functions.ILike(s.Summary, $"%{query}%")`

```
🔍 Sessions matching "trip to portugal":

#42 · 2h ago — Discussed flights, hotel in Lisbon, and Sintra day trip...
#22 · 2w ago — Early trip brainstorming. Mentioned Porto and Algarve...

💡 /resume 42 to continue
```

## Configuration

| Env Var | Default | Purpose |
|---------|---------|---------|
| `LIS_RESUME_TOKEN_BUDGET` | 70% of `ContextBudget` | Max estimated tokens for full session resume |
| `LIS_COMPACTION_THRESHOLD` | 80% of `ContextBudget` | Input tokens to trigger auto-compaction |

Both default to 0 (percentage-based). Set explicitly for fine-grained control.
