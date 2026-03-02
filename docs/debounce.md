# Message Debounce

## Problem

WhatsApp users often send multiple short messages in quick succession instead of one
complete message. Without debouncing, each message triggers a separate AI invocation,
producing fragmented responses and wasting API tokens.

## How It Works

When a message arrives, Lis **immediately** persists it and marks it as read (blue ticks).
Instead of calling the AI right away, it starts a configurable timer. If another message
(or typing event) arrives before the timer fires, the timer resets. Once the timer fires
(no new activity for the configured duration), Lis calls the AI with the full conversation
context — all messages received during the debounce window are already in the database and
included in the context.

```
User sends "Hey"        ->  persist + mark read  ->  start 3s timer
User sends "How are u"  ->  persist + mark read  ->  reset 3s timer
User sends "?"          ->  persist + mark read  ->  reset 3s timer
... 3 seconds of silence ...
Timer fires  ->  AI sees all 3 messages  ->  responds once, replies to "?"
```

The AI response quotes the **last** message the user sent, which is the most natural
behavior.

## Configuration

| Variable                | Default | Description                            |
|-------------------------|---------|----------------------------------------|
| `LIS_MESSAGE_DEBOUNCE_MS` | `3000`  | Milliseconds to wait after the last message before responding. Set to `0` to disable debouncing (immediate response). |

## Typing Event Support

Lis is ready to handle incoming `chat_presence` webhook events from GOWA. When a typing
event arrives during an active debounce window, the timer resets — preventing the AI from
responding while the user is still composing their next message.

**Status:** GOWA does not yet forward incoming `ChatPresence` events via webhook. There is
an open PR to add this:
[go-whatsapp-web-multidevice PR #547](https://github.com/aldinokemal/go-whatsapp-web-multidevice/pull/547).
Once merged, typing-based debounce will work automatically with no changes needed in Lis.

## Architecture

The feature uses the **decorator pattern**:

- `ConversationService` (scoped) handles message persistence, AI invocation, and response
  sending. It implements `IConversationService` and exposes two phases:
  - `IngestMessageAsync` — persist + mark read
  - `RespondAsync` — AI processing + send response
- `MessageDebouncer` (singleton) implements `IConversationService` and decorates
  `ConversationService`. It calls `IngestMessageAsync` immediately and debounces
  `RespondAsync` using per-chat timers with `CancellationTokenSource`.

This preserves the `IConversationService` abstraction for multi-channel support.
