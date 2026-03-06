# Per-Chat Response Queue + `/abort` Command

## Context

No per-chat mutual exclusion for AI response processing. When multiple messages arrive while an AI response is in progress, `MessageDebouncer.RespondInScopeAsync` fires concurrent `RespondAsync` calls for the same chat. This corrupts chat history — messages interleave with AI responses, causing Claude API errors: `"This model does not support assistant message prefill. The conversation must end with a user message."`

Root cause: `RespondInScopeAsync` passes `CancellationToken.None` and has no concurrency control beyond debouncing.

---

## Design

### Core Principle

**No writes to `message` table (that the AI can see) while an AI response is in progress.** New messages arriving during a response are persisted with `queued = true`. The AI's context query excludes queued messages. After the response, queued messages are flushed (timestamps updated to NOW so they sort after AI responses), commands executed, and a new AI response triggered if needed.

### Per-Chat `ChatState`

Replace `_locks` + `_pending` with `ConcurrentDictionary<string, ChatState>`:

```
ChatState:
  Lock           (object)                   — guards debounce state mutations (sync)
  Gate           (SemaphoreSlim(1,1))       — mutual exclusion for AI responses
  IsResponding   (volatile bool)            — true while AI is responding (race-free flag)
  ActiveCts      (CancellationTokenSource?) — in-progress AI response cancellation
  PendingMessage (IncomingMessage?)         — debounce: latest message waiting for timer
  DebounceCts    (CancellationTokenSource?) — debounce timer cancellation
  ReactedIds     (List<string>)             — external IDs with 🕐 reactions to clear
```

### Message Flow — Two Modes

**When no AI is responding** (`IsResponding == false`):
- Messages ingested normally (`queued = false`)
- Commands execute immediately
- Normal messages → debounce → AI response

**When AI is responding** (`IsResponding == true`):
- ALL messages (including commands) ingested with `queued = true`
- React 🕐 on message
- `/abort` additionally: cancel `ActiveCts` + pending debounce
- After AI response → flush loop processes queued messages

### Flush Loop (inside semaphore, after AI response)

```
while true:
  1. UPDATE message SET queued = false, timestamp = NOW() WHERE queued AND chat_id = X
  2. For each flushed command: execute, send response, persist response
  3. Remove 🕐 reactions
  4. If last flushed message was a command → no AI response → break
  5. If last flushed message was a user message → AI response → loop again
  6. If no flushed messages → break
```

### `/abort`

Handled same as other messages during AI response — ingested with `queued = true`. Additionally cancels `ActiveCts` so the AI stops early. When the flush loop processes `/abort`, it executes the command and since the last message is a command, no new AI response is triggered.

When no AI is running: `/abort` executes immediately like any command.

### Crash Resilience

**`queued` flag on `MessageEntity`** is the sole persistence mechanism. On startup: flush all queued messages and trigger AI responses for chats that had them. No separate `NeedsResponse` flag needed — the presence of queued messages IS the signal.

Note: if the app crashes during an AI response with NO queued messages (the triggering message was not queued), the response is lost. The user re-sends — acceptable tradeoff for simplicity.

### Concurrency Model

`ChatState` is **per-chat** (keyed by `chatId` in `ConcurrentDictionary`). Multiple chats process in full parallel. The `SemaphoreSlim` only serializes AI responses within the **same** chat.

---

## Implementation

### Step 1 — Schema change

**`Lis.Persistence/Entities/MessageEntity.cs`** — add:
```csharp
[Column("queued")]
[JsonPropertyName("queued")]
public bool Queued { get; set; }
```

**Migration** — single migration for the new column.

### Step 2 — Add `ReactAsync` to `IChannelClient`

**`Lis.Core/Channel/IChannelClient.cs`** — add:
```csharp
Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default);
```

**`Lis.Channels/WhatsApp/WhatsAppClient.cs`** — implement via existing `GowaClient.ReactToMessageAsync`:
```csharp
[Trace("WhatsAppClient > ReactAsync")]
public async Task ReactAsync(string messageId, string chatId, string emoji, CancellationToken ct = default) {
    await gowa.ReactToMessageAsync(messageId, StripJidSuffix(chatId), emoji, ct);
}
```

### Step 3 — Create `AbortCommand`

**New: `Lis.Agent/Commands/AbortCommand.cs`**:
```csharp
public sealed class AbortCommand : IChatCommand {
    public string[] Triggers => ["/abort", "/stop", "/cancel"];
    public Task<string> ExecuteAsync(CommandContext ctx, CancellationToken ct)
        => Task.FromResult("⛔ Aborted.");
}
```

**`Lis.Agent/AgentSetup.cs`** — register alongside existing commands.

### Step 4 — `queued` parameter in `ConversationService`

**`Lis.Agent/ConversationService.cs`**:

**`IngestMessageAsync`** — add `bool queued` parameter, pass to `PersistMessageAsync`:
```csharp
public async Task<(ChatEntity Chat, bool ShouldRespond)> IngestMessageAsync(
    IncomingMessage message, bool queued, CancellationToken ct) {
    ...
    await PersistMessageAsync(db, session, message, queued, ct);
    ...
    return (chat, this.ShouldRespond(message));
}
```

**`PersistMessageAsync`** — add `bool queued`, set on entity:
```csharp
private static async Task PersistMessageAsync(
    LisDbContext db, SessionEntity session, IncomingMessage message, bool queued, CancellationToken ct) {
    MessageEntity entity = new() {
        ...
        Queued = queued,
    };
    ...
}
```

**Context query** (line 127) — exclude queued:
```csharp
List<MessageEntity> recentMessages = await db.Messages
    .Where(m => m.SessionId == session.Id && !m.Queued)
    .OrderBy(m => m.Timestamp)
    .ToListAsync(ct);
```

**Also add `!m.Queued`** to queries in:
- `CheckCompactionTriggersAsync` (lines 225, 235, 249, 258)
- `CompactCommand.ExecuteAsync`
- `StatusCommand.ExecuteAsync`
- `CompactionService.CompactAsync`
- `CompactionService.GenerateSessionSummaryAsync`

### Step 5 — Refactor `MessageDebouncer`

**`Lis.Agent/MessageDebouncer.cs`** — full rewrite:

**a) State:**
```csharp
private readonly ConcurrentDictionary<string, ChatState> _chats = new();

private ChatState GetChatState(string chatId) =>
    _chats.GetOrAdd(chatId, _ => new ChatState());

private sealed class ChatState {
    public readonly object        Lock = new();
    public readonly SemaphoreSlim Gate = new(1, 1);
    public volatile bool          IsResponding;
    public CancellationTokenSource? ActiveCts;
    public IncomingMessage?         PendingMessage;
    public CancellationTokenSource? DebounceCts;
    public readonly List<string>  ReactedIds = [];
}
```

**b) Constructor:** inject `IChannelClient channelClient`.

**c) `HandleIncomingAsync`** — single unified path:
```csharp
public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
    ChatState state = this.GetChatState(message.ChatId);
    bool isQueued = state.IsResponding;

    // 1. Ingest (persisted with queued flag)
    bool shouldRespond;
    using (IServiceScope scope = scopeFactory.CreateScope()) {
        ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
        (_, shouldRespond) = await svc.IngestMessageAsync(message, isQueued, ct);
    }
    if (!shouldRespond) return;

    // 2. If AI is responding: everything is queued, just react 🕐
    if (isQueued) {
        // /abort additionally cancels the active AI response
        if (message.Body?.Trim() is "/abort" or "/stop" or "/cancel") {
            state.ActiveCts?.Cancel();
            CancelPendingDebounce(state);
        }
        // React 🕐 (best-effort)
        try {
            await channelClient.ReactAsync(message.ExternalId, message.ChatId, "🕐");
            lock (state.Lock) { state.ReactedIds.Add(message.ExternalId); }
        } catch { }
        return;
    }

    // 3. No AI running — handle normally
    // Commands: execute immediately
    if (commandRouter.Match(message.Body) is not null) {
        await this.ExecuteCommandAsync(message);
        return;
    }

    // Normal messages: debounce → AI response
    int debounceMs = lisOptions.Value.MessageDebounceMs;
    if (debounceMs <= 0) { await this.RespondInScopeAsync(message); return; }
    this.ScheduleDebounce(message.ChatId, message, debounceMs);
}
```

**d) `ExecuteCommandAsync`** — commands without semaphore:
```csharp
private async Task ExecuteCommandAsync(IncomingMessage message) {
    try {
        using IServiceScope scope = scopeFactory.CreateScope();
        ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await svc.RespondAsync(message, CancellationToken.None);
    } catch (Exception ex) {
        logger.LogError(ex, "Error executing command in {ChatId}", message.ChatId);
    }
}
```

**e) `RespondInScopeAsync`** — AI response + flush loop:
```csharp
private async Task RespondInScopeAsync(IncomingMessage message) {
    ChatState state = this.GetChatState(message.ChatId);

    await state.Gate.WaitAsync();
    state.IsResponding = true;
    try {
        // Initial AI response
        using CancellationTokenSource cts = new();
        state.ActiveCts = cts;
        try {
            using IServiceScope scope = scopeFactory.CreateScope();
            ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
            await svc.RespondAsync(message, cts.Token);
        } catch (OperationCanceledException) {
            logger.LogInformation("Response cancelled for {ChatId}", message.ChatId);
        } catch (Exception ex) {
            logger.LogError(ex, "Error responding to {MessageId} in {ChatId}",
                message.ExternalId, message.ChatId);
        } finally {
            Interlocked.CompareExchange(ref state.ActiveCts, null, cts);
        }

        // Flush loop: process queued messages
        while (true) {
            bool needsResponse = await this.FlushQueueAsync(message.ChatId, state);
            if (!needsResponse) break;

            // New AI response for flushed messages
            using CancellationTokenSource cts2 = new();
            state.ActiveCts = cts2;
            try {
                using IServiceScope scope2 = scopeFactory.CreateScope();
                ConversationService svc2 = scope2.ServiceProvider.GetRequiredService<ConversationService>();
                // Use original message for ChatId — context comes from DB
                await svc2.RespondAsync(message, cts2.Token);
            } catch (OperationCanceledException) {
                logger.LogInformation("Response cancelled for {ChatId}", message.ChatId);
            } catch (Exception ex) {
                logger.LogError(ex, "Error responding in {ChatId}", message.ChatId);
            } finally {
                Interlocked.CompareExchange(ref state.ActiveCts, null, cts2);
            }
        }
    } finally {
        state.IsResponding = false;
        state.Gate.Release();
    }
}
```

**f) `FlushQueueAsync`** — flush queued messages, execute commands, return whether AI response needed:
```csharp
private async Task<bool> FlushQueueAsync(string chatId, ChatState state) {
    // Remove 🕐 reactions
    List<string> reacted;
    lock (state.Lock) { reacted = [..state.ReactedIds]; state.ReactedIds.Clear(); }
    foreach (string id in reacted)
        try { await channelClient.ReactAsync(id, chatId, ""); } catch { }

    using IServiceScope scope = scopeFactory.CreateScope();
    LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

    ChatEntity? chat = await db.Chats.Include(c => c.CurrentSession)
        .FirstOrDefaultAsync(c => c.ExternalId == chatId);
    if (chat?.CurrentSession is null) return false;

    List<MessageEntity> queued = await db.Messages
        .Where(m => m.ChatId == chat.Id && m.Queued)
        .OrderBy(m => m.Id)
        .ToListAsync();
    if (queued.Count == 0) return false;

    // Flush: set queued = false, update timestamps to sort AFTER AI responses
    DateTimeOffset now = DateTimeOffset.UtcNow;
    foreach (MessageEntity msg in queued) {
        msg.Queued    = false;
        msg.Timestamp = now;
        now           = now.AddMilliseconds(1);
    }
    await db.SaveChangesAsync();

    // Execute queued commands
    SessionEntity session = chat.CurrentSession;
    foreach (MessageEntity msg in queued) {
        if (commandRouter.Match(msg.Body) is not { } match) continue;

        IncomingMessage im = new() {
            ExternalId = msg.ExternalId ?? "", ChatId = chatId,
            SenderId = msg.SenderId ?? "", Body = msg.Body, Timestamp = msg.Timestamp
        };
        CommandContext ctx = new(im, chat, session, db, match.Args);
        string response = await match.Command.ExecuteAsync(ctx, CancellationToken.None);
        await channelClient.SendMessageAsync(chatId, response, msg.ExternalId);
        db.Messages.Add(new MessageEntity {
            ChatId = chat.Id, SessionId = session.Id,
            SenderId = "me", IsFromMe = true, Role = "assistant",
            Body = response, Timestamp = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // AI response needed only if last queued message is NOT a command
    return commandRouter.Match(queued[^1].Body) is null;
}
```

**g) `ScheduleDebounce`, `StartTimer`, `HandleTypingAsync`** — same logic, migrated to `ChatState` fields.

**h) `CancelPendingDebounce` helper:**
```csharp
private static void CancelPendingDebounce(ChatState state) {
    lock (state.Lock) {
        state.DebounceCts?.Cancel();
        state.DebounceCts?.Dispose();
        state.DebounceCts    = null;
        state.PendingMessage = null;
    }
}
```

**i) `TriggerPendingResponsesAsync`** — startup crash recovery:
```csharp
public async Task TriggerPendingResponsesAsync() {
    using IServiceScope scope = scopeFactory.CreateScope();
    LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

    // Find chats with queued messages (leftover from crash)
    var chatIds = await db.Messages
        .Where(m => m.Queued)
        .Select(m => m.ChatId)
        .Distinct()
        .ToListAsync();
    if (chatIds.Count == 0) return;

    // Flush: make queued messages visible with current timestamps
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE message SET queued = false, timestamp = NOW() WHERE queued");

    // Trigger AI response for each affected chat
    foreach (long chatId in chatIds) {
        ChatEntity? chat = await db.Chats.FindAsync(chatId);
        if (chat is null) continue;

        MessageEntity? lastMsg = await db.Messages
            .Where(m => m.ChatId == chatId && m.Role == null)
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync();
        if (lastMsg is null) continue;

        IncomingMessage synthetic = new() {
            ExternalId = lastMsg.ExternalId ?? $"recovery-{chat.ExternalId}",
            ChatId     = chat.ExternalId,
            SenderId   = lastMsg.SenderId ?? "",
            SenderName = lastMsg.SenderName,
            Timestamp  = lastMsg.Timestamp,
            Body       = lastMsg.Body
        };
        _ = Task.Run(() => this.RespondInScopeAsync(synthetic));
    }
}
```

**j) `Dispose`** — dispose all `ChatState.Gate` and `ChatState.DebounceCts`.

### Step 6 — Startup re-trigger

**`Lis.Api/Program.cs`** — after migrations, before `app.RunAsync()`:
```csharp
if (app.Services.GetService<IConversationService>() is MessageDebouncer debouncer)
    await debouncer.TriggerPendingResponsesAsync();
```

---

## Files

| File | Action |
|------|--------|
| `Lis.Core/Channel/IChannelClient.cs` | **MODIFY** — add `ReactAsync` |
| `Lis.Channels/WhatsApp/WhatsAppClient.cs` | **MODIFY** — implement `ReactAsync` |
| `Lis.Persistence/Entities/MessageEntity.cs` | **MODIFY** — add `Queued` |
| `Lis.Persistence/Migrations/` | **NEW** — migration for `queued` column |
| `Lis.Agent/ConversationService.cs` | **MODIFY** — `queued` param on ingest, `!m.Queued` on context query |
| `Lis.Agent/MessageDebouncer.cs` | **REWRITE** — ChatState, semaphore, IsResponding flag, queued ingestion, flush loop, reactions, crash recovery |
| `Lis.Agent/Commands/AbortCommand.cs` | **NEW** — `/abort`, `/stop`, `/cancel` |
| `Lis.Agent/AgentSetup.cs` | **MODIFY** — register AbortCommand |
| `Lis.Api/Program.cs` | **MODIFY** — call `TriggerPendingResponsesAsync` on startup |
| `Lis.Agent/Commands/CompactCommand.cs` | **MODIFY** — add `!m.Queued` to queries |
| `Lis.Agent/Commands/StatusCommand.cs` | **MODIFY** — add `!m.Queued` to queries |
| `Lis.Agent/CompactionService.cs` | **MODIFY** — add `!m.Queued` to queries |
| `Plans/message-queue-and-abort.md` | **NEW** — copy of this plan |
| `docs/MESSAGE_QUEUE.md` | **NEW** — how queuing, flush loop, and crash recovery work |

---

### Step 7 — Documentation

**New: `Plans/message-queue-and-abort.md`** — save this plan file.

**New: `docs/MESSAGE_QUEUE.md`** — document:
- Per-chat queuing model: `ChatState`, `SemaphoreSlim`, `IsResponding` flag
- The `queued` flag on messages: when set, when cleared, how AI context excludes them
- Two modes: normal flow vs queued flow (when AI is responding)
- Flush loop: timestamp update, command execution, AI re-trigger decision
- `/abort` command: triggers cancellation + gets queued like other messages
- 🕐 reaction indicator: added on queue, removed on flush
- Crash recovery: startup flushes all `queued = true` messages, triggers AI for affected chats
- Concurrency: per-chat semaphore, multiple chats in parallel

**Update: `docs/CONTEXT_COMPACTION.md`** — add note that all message queries now filter `!m.Queued`.

---

## Verify

- `dotnet build` clean
- Migration applies: `dotnet ef database update`
- Send message → AI responds normally (no regression)
- Send message while AI responding → 🕐 reaction → message queued in DB → after AI finishes: 🕐 removed, message flushed (timestamp updated), AI responds to full context
- Send `/status` while AI responding → queued → after AI finishes: status executes, no AI re-trigger
- Send `/abort` while AI responding → AI cancelled early → queue flushed → /abort executed → "⛔ Aborted." → no AI re-trigger
- Send multiple messages + commands while AI responding → all queued → flushed in order → commands executed → AI responds if last was user message
- Kill app with queued messages → restart → queued messages flushed, pending chats re-triggered
- No `"conversation must end with a user message"` errors
