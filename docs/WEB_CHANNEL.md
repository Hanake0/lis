# Web Channel

Provider-agnostic web channel for Lis. Exposes a WebSocket endpoint that any web client
(VSCode extension, browser app, etc.) can connect to, send messages, and receive AI
responses in real time. Acts as a peer to the WhatsApp channel — both implement
`IChannelClient` and feed `IConversationService`.

## Wire Protocol

### Endpoint

```
GET /web/ws?token=<shared-secret>
```

* Validates `token` against `WEB_CHANNEL_TOKEN`. Mismatch → 401, the upgrade is rejected.
* On accept, the server sends `{"type":"connected"}` as the first frame.
* A connection has **no chat binding**. Frames in both directions carry `chat_id`.

### Server → Client Frames

All frames have a `type` discriminator. Null fields are omitted on the wire.

| `type`           | Payload                                              | When                                                  |
| ---------------- | ---------------------------------------------------- | ----------------------------------------------------- |
| `connected`      | —                                                    | First frame after upgrade.                            |
| `message`        | `chat_id`, `id`, `text`, `role`, `timestamp`         | Any persisted message (user echo OR assistant reply). |
| `typing`         | `chat_id`                                            | Assistant is composing.                               |
| `typing_stopped` | `chat_id`                                            | Assistant stopped composing.                          |
| `reaction`       | `chat_id`, `id` (message id), `emoji`                | Reaction added (or removed: `emoji=""`).              |
| `chat_list`      | `chats: [{chat_id, name, updated_at}]`               | Response to `list_chats`.                             |
| `chat_history`   | `chat_id`, `messages: [{id, role, text, timestamp}]` | Response to `history`.                                |
| `error`          | `error`, optional `chat_id`                          | Bad input or processing failure.                      |

Frames are **broadcast** to every connected client. Clients filter inbound frames by
`chat_id` to render only the active chat (or render all of them — that's the client's
choice). The server keeps no per-connection routing state.

### Client → Server Frames

| `type`       | Payload                                                                                          |
| ------------ | ------------------------------------------------------------------------------------------------ |
| `message`    | `chat_id` (required), `text` (required), optional `dispatch` (one of `default`/`immediate`/`queue_only`/`interrupt` — defaults to `immediate`). |
| `history`    | `chat_id` (required), optional `limit` (1–500, default 100).                                     |
| `list_chats` | —                                                                                                |

### Dispatch Modes

Maps to `MessageDispatchMode` in `Lis.Core.Channel`. Lets the web client opt out of
WhatsApp-style debounce per message:

* `immediate` *(default for web)* — skip the debounce timer, respond now. Still waits on the per-chat serialization gate.
* `default` — same as WhatsApp: debounce timer, queue-if-already-responding.
* `queue_only` — persist with `queued=true` and do **not** trigger a response. Useful for "draft" or batched messages.
* `interrupt` — cancel the active AI response (if any), then respond.

## Architecture

```
┌────────────────────────────────────────────────────────┐
│              WebChannelClient (singleton)              │
│                                                        │
│  ConcurrentDictionary<Guid, (WebSocket, SemaphoreSlim)>│
│                                                        │
│  Exposes IChannelClient (used by ConversationService)  │
│  Exposes Register/Unregister (used by Controller)      │
└─────────────┬───────────────────────────▲──────────────┘
              │ broadcast                 │ register / unregister
              │ (per-socket lock)         │
              ▼                           │
┌─────────────────────────────┐    ┌─────────────────────┐
│  Each connected WebSocket   │◄───┤ WebChannelController│
│  (sole writer = publisher)  │    │  (reader loop)      │
└─────────────────────────────┘    └─────────────────────┘
```

**Why one class, not a broker + client?** WebSocket is point-to-point — some collection of
open sockets has to live somewhere. At single-user scale the simplest possible shape works:
a flat dict broadcast to all, clients filter by `chat_id`. No per-subscriber `Channel<T>`
queue, no writer loop in the controller. The only synchronization is a per-socket
`SemaphoreSlim` because `WebSocket.SendAsync` is not concurrent-safe.

**Auto-vivify chats.** Clients mint chat ids locally (`web:<uuid>`). The chat row is
created on the first `message` frame referencing that id — no separate `new_chat` round
trip, no orphan rows.

## Configuration

Set in `.env`:

```bash
WEB_CHANNEL_ENABLED=true
WEB_CHANNEL_TOKEN=<long-random-secret>
WEB_CHANNEL_OWNER_ID=web:owner          # sender identity for auth
WEB_CHANNEL_OWNER_NAME=Me               # optional display name
WEB_CHANNEL_CHAT_PREFIX=web:            # used when minting chat ids
```

Only one channel needs to be active for the conversation pipeline to start — Web and
WhatsApp can both be on, but the agent currently resolves a single `IChannelClient` via DI.
If both are enabled, the DI container picks the last registration; first cut deployments
should pick one.

## Limitations (First Cut)

* No streaming — assistant messages arrive as complete frames once the AI loop emits text.
* No tool-call cards — tool calls happen server-side, only the final assistant text shows up.
* No media uploads from the web client.
* Only one `IChannelClient` is wired into `ConversationService` at a time. Multi-channel routing is future work.

## Manual Test

```bash
# Server side
WEB_CHANNEL_ENABLED=true WEB_CHANNEL_TOKEN=test123 ANTHROPIC_ENABLED=true \
  dotnet run --project Lis.Api

# Client side
websocat 'ws://localhost:3010/web/ws?token=test123'
# Then paste:
{"type":"message","chat_id":"web:demo","text":"hello, who are you?"}
```

You should see typing frames followed by a `message` frame containing the assistant's
reply.
