# Message Queue & `/abort`

## Overview

Per-chat message queuing ensures only one AI response runs at a time per chat. Messages
arriving during an AI response are persisted with `queued = true` (crash-safe but invisible
to the AI context). After the AI responds, queued messages are flushed and processed.

## Concurrency Model

`MessageDebouncer` maintains a `ConcurrentDictionary<string, ChatState>` keyed by chat ID.
Multiple chats process in full parallel. Each `ChatState` has:

- **`Gate`** (`SemaphoreSlim(1,1)`) — mutual exclusion for AI responses within the same chat
- **`IsResponding`** (`volatile bool`) — set inside the semaphore, cleared before release.
  Eliminates the race between `HandleIncomingAsync` checking the flag and the semaphore release
- **`ActiveCts`** (`CancellationTokenSource?`) — cancellation for the current AI response.
  Cleaned up via `Interlocked.CompareExchange` so each response only clears its own CTS
- **`ReactedIds`** — external IDs with clock reactions to remove after flush

## Message Flow

### Normal Mode (`IsResponding == false`)

1. Message arrives at `HandleIncomingAsync`
2. Ingested via `ConversationService.IngestMessageAsync` with `queued = false`
3. Commands execute immediately (no semaphore needed)
4. Normal messages debounce, then enter `RespondInScopeAsync` (acquires semaphore)

### Queued Mode (`IsResponding == true`)

1. Message arrives at `HandleIncomingAsync`
2. `IsResponding` is `true` — message ingested with `queued = true`
3. Clock reaction added to message (visual indicator)
4. `/abort`, `/stop`, `/cancel` additionally cancel `ActiveCts` and pending debounce
5. After AI response finishes, the **flush loop** runs (still inside semaphore)

## The `queued` Flag

- **`MessageEntity.Queued`** (`bool`, column `queued`) — persisted in the database
- AI context query filters `WHERE ... AND NOT queued` — queued messages are invisible to AI
- All message queries across the codebase filter `!m.Queued`:
  - `ConversationService.RespondAsync` — context loading
  - `ConversationService.CheckCompactionTriggersAsync` — compaction split calculation
  - `CompactCommand.ExecuteAsync`
  - `StatusCommand.ExecuteAsync` — message count
  - `CompactionService.CompactAsync` — message loading for summarization
  - `CompactionService.GenerateSessionSummaryAsync`

## Flush Loop

After each AI response (inside semaphore):

1. **Remove clock reactions** (best-effort, empty emoji = remove)
2. **Load queued messages** from DB for the chat, ordered by ID
3. **Flush**: set `queued = false`, update timestamps to `NOW()` (incremented by 1ms each)
   so they sort chronologically after the AI's response messages
4. **Execute queued commands**: for each flushed message that matches a command, run it,
   send the response, persist the response
5. **Decide**: if the last flushed message was a user message (not a command), trigger
   another AI response. If it was a command (or no messages were flushed), break the loop

The loop repeats because new messages may arrive during the flush.

## `/abort` Command

**Triggers**: `/abort`, `/stop`, `/cancel`

When AI is responding:
- Queued like other messages (`queued = true`)
- Additionally cancels `ActiveCts` — AI stops early via `OperationCanceledException`
- Flush loop processes it as a command: sends "Aborted." response
- Since last message is a command, no new AI response is triggered

When no AI is running: executes immediately like any other command.

## Clock Reaction Indicator

- When a message is queued, a clock emoji reaction is added to it
- When the flush loop runs, all clock reactions are removed (empty string = remove reaction)
- Both operations are best-effort (failures silently ignored)

## Crash Recovery

On startup, `TriggerPendingResponsesAsync` runs:

1. Find all messages with `queued = true` (leftover from crash)
2. Flush them: `UPDATE message SET queued = false, timestamp = NOW() WHERE queued`
3. For each affected chat, construct a synthetic `IncomingMessage` from the last user message
4. Fire `RespondInScopeAsync` for each (via `Task.Run`)

**Note**: if the app crashes during an AI response with NO queued messages (the triggering
message was already `queued = false`), the AI response is lost. The user re-sends — acceptable
tradeoff for simplicity.

## Key Files

| File | Role |
|------|------|
| `Lis.Agent/MessageDebouncer.cs` | Queue orchestration, flush loop, crash recovery |
| `Lis.Agent/ConversationService.cs` | `queued` param on ingest, `!m.Queued` on context |
| `Lis.Agent/Commands/AbortCommand.cs` | `/abort`, `/stop`, `/cancel` command |
| `Lis.Persistence/Entities/MessageEntity.cs` | `Queued` column |
| `Lis.Core/Channel/IChannelClient.cs` | `ReactAsync` for clock indicator |
