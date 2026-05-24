# Plan: Web Channel + VSCode Extension (First Cut)

## Context

Lis is a personal AI assistant with provider-agnostic messaging, currently only reachable via WhatsApp. The user wants to build an IDE-native experience — a VSCode extension that talks to Lis over WebSocket — as the foundation for a future flow-based agent orchestration system (described in the conversation transcript). This first cut is the minimal wire-up: send a message from VSCode, get an AI response back.

**Goal:** Prove the end-to-end path: VSCode webview → WebSocket → Lis conversation pipeline → AI response → WebSocket → webview renders it.

**Constraints:**
- Web extension compatible (no Node APIs — must work in vscode.dev, code-server, and desktop VSCode)
- Only ONE channel active at a time (Web OR WhatsApp, not both) — multi-channel routing is future work
- First cut: plain chat only. No tool cards, no agent picker, no flow editor.
- A single WebSocket connection can read and write across any number of chats. Every frame (both directions) carries a `chat_id`; the connection itself is not bound to a chat.
- Debounce stays respected; per-message dispatch modes give the web client an explicit way to opt out of debounce without bypassing the abstraction.

---

## Part 1a: Core changes — explicit message dispatch modes

The current `MessageDebouncer` has implicit behaviors:
- AI not responding → ingest, start debounce timer, respond after debounce
- AI responding → ingest with `queued=true`, no new response; queue is flushed after AI finishes

The web client needs to opt out of debounce on a per-message basis without hardcoding "web = skip debounce" inside the debouncer. The cleanest way is to attach an explicit dispatch mode to each `IncomingMessage`, so channels declare *intent* and the debouncer dispatches accordingly. This keeps existing WhatsApp behavior (Default mode) unchanged.

### Files to create

**`Lis.Core/Channel/MessageDispatchMode.cs`**
```csharp
public enum MessageDispatchMode {
    Default,        // Existing: debounce timer + queue-if-responding (WhatsApp)
    Immediate,      // Skip debounce, respond now (still respects serialization gate)
    QueueOnly,      // Persist with queued=true, do not trigger a response
    Interrupt,      // Cancel current AI response (if any), then respond now
    // Future: InjectIntoTurn — append to in-flight ToolRunner loop. Requires ToolRunner hook; deferred.
}
```

### Files to modify

**`Lis.Core/Channel/IncomingMessage.cs`**
- Add `public MessageDispatchMode Dispatch { get; init; } = MessageDispatchMode.Default;`

**`Lis.Agent/MessageDebouncer.cs`** — extend `HandleIncomingAsync` to branch on `message.Dispatch`:
- `Default`: current code path, no changes
- `Immediate`: ingest with `queued: false`, then call `RespondInScopeAsync` directly (skip debounce timer). Still acquires the per-chat Gate semaphore.
- `QueueOnly`: ingest with `queued: true`, return. No response triggered.
- `Interrupt`: if `state.ActiveCts` is non-null, cancel it. Then ingest + respond like Immediate. The currently-running AI loop gets a CancellationToken trip and exits cleanly via existing cancellation paths.

This adds ~30 lines to MessageDebouncer and one enum check. WhatsApp's `GowaWebhookController` continues to construct `IncomingMessage` without setting `Dispatch`, so it defaults and behavior is identical to today.

The web channel will set `Dispatch = MessageDispatchMode.Immediate` by default, with the client able to override per-frame.

---

## Part 1b: Backend channel — `Lis.Channels/Web/`

All files go inside the existing `Lis.Channels` project (same .csproj). No new project needed.

### Files to create

**`Lis.Channels/Web/WebChannelOptions.cs`**
- Config record: `AuthToken` (shared secret), `OwnerId` (sender identity, default `web:owner`), `OwnerName` (display name), `ChatIdPrefix` (default `web:`)

**`Lis.Channels/Web/WebChannelClient.cs`** (singleton — implements `IChannelClient` and owns the connected sockets)
- Singleton lifetime. `IChannelClient` doesn't *have* to be scoped — the WhatsApp impl is scoped only because it uses per-request HttpClient. Web has no such need.
- Internal state: `ConcurrentDictionary<Guid, Connection>` where `Connection` holds the `WebSocket` and a `SemaphoreSlim(1, 1)` send-lock. NOT keyed by chatId — broadcast to all; clients filter by `chat_id`.
- Connection API (called by `WebChannelController`):
  - `Register(WebSocket socket) → Guid` — adds the connection, returns the id used to remove it.
  - `Unregister(Guid id)` — removes the entry on disconnect.
- `IChannelClient` API (called by `ConversationService`):
  - `SendMessageAsync(chatId, content, ...)`: builds a `message` ServerFrame (chat_id in payload, server-minted GUID id), then calls an internal `PublishAsync(frame)` that iterates every registered connection and, per connection, `await semaphore.WaitAsync()` → `await socket.SendAsync(json, Text, true, ct)` → release. Returns the GUID. Sockets that throw on send are unregistered and disposed inline (best-effort; the controller's reader loop also cleans up on its end). If no subscribers, frame is dropped — message is still persisted by ConversationService, so reconnecting clients fetch it via `history`.
  - `SetTypingAsync` / `StopTypingAsync`: publish typing/typing_stopped frames (chat_id in payload).
  - `MarkReadAsync`: no-op.
  - `ReactAsync`: publish reaction frame.
  - `DownloadMediaAsync`: returns null.
- No `Channel<T>`, no per-subscriber pipe, no writer loop in the controller. A slow socket blocks the publisher for that send (the per-socket semaphore is required because `WebSocket.SendAsync` is not concurrent-safe), but at single-user scale with 1–2 connections that's a non-issue.

### Why a flat list with direct send (no broker, no per-client channels)

WebSocket is point-to-point — some collection of open sockets has to exist somewhere. Since Lis is single-user, the simplest possible shape works: one flat `ConcurrentDictionary` of connections, broadcast to all, clients filter by `chat_id`. No keyed routing, no per-subscriber `Channel<T>` queue, no writer loop in the controller.

The only synchronization needed is a per-socket `SemaphoreSlim` because `WebSocket.SendAsync` is not concurrent-safe. The publisher (`WebChannelClient.SendMessageAsync`) takes the lock per socket, sends, releases. Direct, no indirection.

**`Lis.Channels/Web/WebMessageFormatter.cs`**
- Implements `IMessageFormatter`
- Passthrough — returns content as-is (web client renders markdown natively)

**`Lis.Channels/Web/Schemas/ServerFrame.cs`**
- Discriminated DTO for server→client frames. Types: `connected` (initial handshake, no chat_id), `message`, `typing`, `typing_stopped`, `error`, `chat_list`, `chat_history`
- All chat-scoped frames (`message`, `typing`, `typing_stopped`, `chat_history`, `error` when applicable) carry `chat_id` so clients can filter — the broker is a global broadcast and clients render only frames matching the chat(s) they care about.
- Non-scoped frames: `connected` (handshake), `chat_list` (global list).
- Uses `[JsonPropertyName]` + `[JsonPolymorphic]` or a flat shape with `type` discriminator

**`Lis.Channels/Web/Schemas/ClientFrame.cs`**
- Discriminated DTO for client→server frames. Types: `message`, `list_chats`, `history`
  - (No `auth` — handled by the WS upgrade URL query param. No explicit `new_chat` — clients mint `web:<uuid>` locally; the server creates the row on first `message` with an unknown id. Avoids orphan rows from create-without-send.)
- `message` frame carries `chat_id` (required) + `text` + optional `dispatch` (default `immediate`): `immediate` | `default` | `queue_only` | `interrupt` — maps directly to `MessageDispatchMode` enum
- `history` frame carries `chat_id` (required) + optional `limit`
- `list_chats` carries no payload

**`Lis.Channels/Web/WebChannelController.cs`**
- `[Route("web")]` controller
- `GET /web/ws?token=<token>` — WebSocket upgrade endpoint:
  1. Validate `token` query param against `WebChannelOptions.AuthToken`. If missing/invalid → 401 (reject upgrade).
  2. Accept WebSocket. `Register` with `WebChannelClient`, capture the returned Guid.
  3. Send `{"type": "connected"}` directly (before any other code can publish — there's no race because publish-to-this-socket goes through the same registered entry we just added; if a publish slips in, it's fine to interleave since the send-lock serializes them).
  4. Enter the **reader loop only**: receive client frames, dispatch by type:
     - `message` → look up the chat by the frame's `chat_id`; if it doesn't exist, create the row (auto-vivify on first use — clients mint `web:<uuid>` locally and the row appears only when a message is actually sent, so there are no orphan chats). Construct `IncomingMessage` (ChatId = frame.chat_id, SenderId = OwnerId, Body = text, Dispatch from frame). Call `conversationService.HandleIncomingAsync()` fire-and-forget. Replies arrive back via the same `WebChannelClient` and are pushed to every registered socket (including this one).
     - `history` → query DB scoped to `frame.chat_id`; publish a `chat_history` frame.
     - `list_chats` → query DB; publish a `chat_list` frame.
  5. On reader-loop exit (clean close, error, or cancellation), `Unregister` from `WebChannelClient` and dispose the socket in a `finally`.
- Key detail: a WS connection has NO chat binding. The client sets `chat_id` per outbound frame and filters inbound frames by `chat_id`. One socket handles any number of chats.

**`Lis.Channels/Web/WebChannelSetup.cs`**
- `AddWebChannel()` extension method
- Reads env vars: `WEB_CHANNEL_ENABLED`, `WEB_CHANNEL_TOKEN`, `WEB_CHANNEL_OWNER_ID`, `WEB_CHANNEL_OWNER_NAME`
- Registers: `WebChannelClient` as singleton (exposed as itself AND as `IChannelClient` — same instance, two registrations so the controller can call `Subscribe` while `ConversationService` resolves it as `IChannelClient`), `IMessageFormatter` → `WebMessageFormatter` (singleton).

### Files to modify

**`Lis.Api/Program.cs`**
- Add `app.UseWebSockets()` before `app.MapControllers()`
- Add `if (Env("WEB_CHANNEL_ENABLED") == "true") builder.Services.AddWebChannel();`
- Change the conversation gate from:
  ```
  if (ANTHROPIC_ENABLED && GOWA_ENABLED) { ... }
  ```
  to:
  ```
  bool hasChannel = Env("GOWA_ENABLED") == "true" || Env("WEB_CHANNEL_ENABLED") == "true";
  if (Env("ANTHROPIC_ENABLED") == "true" && hasChannel) { ... }
  ```

**`.env.example`**
- Add Web Channel section with `WEB_CHANNEL_ENABLED`, `WEB_CHANNEL_TOKEN`, `WEB_CHANNEL_OWNER_ID`, `WEB_CHANNEL_OWNER_NAME`

### Singleton lifetime

WhatsApp registers `IChannelClient` as scoped because it uses per-request HTTP clients. Web has no such constraint, so `WebChannelClient` is registered as a singleton. It's the same instance whether resolved as `IChannelClient` (by `ConversationService`) or as `WebChannelClient` (by `WebChannelController` to call `Subscribe`). One instance owns the subscriber list for the lifetime of the app.

Data flow: `ConversationService` → `IChannelClient.SendMessageAsync` → fan-out over registered subscriber channels → each WS controller's writer loop → `SendAsync` to client.

### WS protocol — first cut

Connection lifecycle:
1. Client opens WS to `/web/ws?token=<token>`
2. Server validates token at upgrade time; rejects with 401 if invalid
3. Server sends `{"type": "connected"}` as first frame. No chat binding.
4. Client sends `{"type": "message", "chat_id": "web:abc", "text": "hello", "dispatch": "immediate"}` (server auto-creates the chat row if it's new)
5. Server fires HandleIncomingAsync; WebChannelClient publishes the reply frame (carrying `chat_id`) to the broker; every connected socket receives it; client filters by `chat_id` and renders
6. Typing indicators flow as `{"type": "typing", "chat_id": "..."}` / `{"type": "typing_stopped", "chat_id": "..."}`
7. Client can send `{"type": "history", "chat_id": "..."}` or `{"type": "list_chats"}` at any time. To create a new chat, the client just mints `web:<uuid>` locally and sends a `message` with that id — the server creates the row when the first message lands.

One socket can handle any number of chats — the client picks per outbound frame and filters per inbound frame. No per-chat server objects, no per-chat sockets.

---

## Part 2: Frontend — `clients/vscode/`

### Structure

```
clients/vscode/
  package.json              # Extension manifest (web extension)
  tsconfig.json             # TypeScript config
  esbuild.mjs               # Build script (browser target)
  .vscodeignore             # Exclude from .vsix package
  src/
    extension.ts            # Activate, register providers, manage LisClient lifecycle
    lisClient.ts            # WebSocket wrapper: connect, auth, send/receive typed frames
    chatViewProvider.ts     # WebviewViewProvider: creates chat webview, bridges messages
  media/
    chat.html               # Webview HTML template
    chat.css                # Styles (dark/light theme support via VS Code CSS variables)
    chat.js                 # Webview runtime: message rendering, input handling, scroll
```

### `package.json`
- `name`: `lis-vscode`
- `displayName`: `Lis`
- `browser`: `./dist/extension.js` (web extension entry point — no `main` field)
- `contributes`:
  - `viewsContainers.activitybar`: Lis icon in the activity bar
  - `views.lis-sidebar`: Chat view (type: webview)
  - `commands`: `lis.connect`, `lis.newChat`
  - `configuration`: `lis.serverUrl` (default `ws://localhost:3010/web/ws`), `lis.authToken`
- `engines.vscode`: `^1.100.0`
- `devDependencies`: `@types/vscode`, `esbuild`, `typescript`

### `src/extension.ts`
- `activate(context)`: read config (`lis.serverUrl`, `lis.authToken`), create `LisClient`, register `ChatViewProvider`, register commands
- `deactivate()`: close LisClient

### `src/lisClient.ts`
- Class `LisClient` wrapping a `WebSocket`
- `connect(url, token)`: open WS, send auth frame, wait for authenticated
- `send(frame: ClientFrame)`: serialize and send
- `onMessage(handler)`: register callback for incoming ServerFrames
- `disconnect()`: close cleanly
- `state`: connected/disconnected/authenticating
- Event emitter pattern for `onMessage`, `onConnected`, `onDisconnected`, `onError`
- Reconnect logic: exponential backoff on disconnect

### `src/chatViewProvider.ts`
- Implements `vscode.WebviewViewProvider`
- `resolveWebviewView()`: set webview HTML (load chat.html with nonce CSP), wire up message bridge
- Bridge: webview posts `{type: 'send', text: '...'}` → provider calls `lisClient.send(message frame)` → lisClient receives response → provider posts `{type: 'message', ...}` to webview
- Handles: connection status display, chat switching

### `media/chat.html`
- Minimal chat UI: message list + input box + send button
- Uses VS Code CSS variables for theming (`--vscode-editor-background`, `--vscode-editor-foreground`, etc.)
- CSP meta tag with nonce for scripts

### `media/chat.js`
- Acquires VS Code API via `acquireVsCodeApi()`
- Renders messages as simple HTML (markdown rendering deferred to later)
- Handles: input submit (enter key + button), scroll to bottom on new message, typing indicator
- Filters incoming frames by `chat_id` (matches the current chat the webview is showing; drops the rest — the broker is global broadcast)
- Posts to extension: `{type: 'send', text}`, `{type: 'newChat'}`, `{type: 'ready'}`
- Receives from extension: `{type: 'message', id, chat_id, text, role}`, `{type: 'typing', chat_id, active}`, `{type: 'connected', chat_id}`, `{type: 'status', state}`

### `media/chat.css`
- Dark/light adaptive via VS Code CSS variables
- Message bubbles: user right-aligned, assistant left-aligned
- Typing indicator: animated dots
- Input area: fixed at bottom, textarea + send button

---

## Files summary

### Create (backend — 8 files)
1. `Lis.Core/Channel/MessageDispatchMode.cs` (new enum)
2. `Lis.Channels/Web/WebChannelOptions.cs`
3. `Lis.Channels/Web/WebChannelClient.cs` (publisher + subscriber registry, singleton)
4. `Lis.Channels/Web/WebMessageFormatter.cs`
5. `Lis.Channels/Web/Schemas/ServerFrame.cs`
6. `Lis.Channels/Web/Schemas/ClientFrame.cs`
7. `Lis.Channels/Web/WebChannelController.cs`
8. `Lis.Channels/Web/WebChannelSetup.cs`

### Create (frontend — 8 files)
10. `clients/vscode/package.json`
11. `clients/vscode/tsconfig.json`
12. `clients/vscode/esbuild.mjs`
13. `clients/vscode/.vscodeignore`
14. `clients/vscode/src/extension.ts`
15. `clients/vscode/src/lisClient.ts`
16. `clients/vscode/src/chatViewProvider.ts`
17. `clients/vscode/media/chat.html` + `chat.css` + `chat.js`

### Modify (4 files)
17. `Lis.Core/Channel/IncomingMessage.cs` — add `Dispatch` property
18. `Lis.Agent/MessageDebouncer.cs` — branch on `message.Dispatch`
19. `Lis.Api/Program.cs` — WebSocket middleware + web channel registration + conversation gate
20. `.env.example` — Web channel env vars

---

## Verification

1. **Backend builds:** `dotnet build` from repo root — no errors
2. **Extension builds:** `cd clients/vscode && npm install && npm run build` — produces `dist/extension.js`
3. **Integration test (manual):**
   - Set `WEB_CHANNEL_ENABLED=true`, `WEB_CHANNEL_TOKEN=test123`, `ANTHROPIC_ENABLED=true` in `.env`
   - Run `cd Lis.Api && dotnet run`
   - Use a WebSocket client (e.g., `websocat ws://localhost:3010/web/ws`) to send auth + message frames
   - Verify: auth succeeds, message triggers AI response, response arrives as a server frame
4. **Extension test:** Load extension in VS Code (`F5` launch), open Lis sidebar, type a message, see response

## Known limitations (first cut)

- Only ONE channel active at a time (Web OR WhatsApp)
- No streaming — full assistant messages only (same as WhatsApp)
- No tool call cards in UI — tool calls happen server-side, only final text response shown
- No agent picker / model switcher — uses default agent
- No chat history persistence in the extension (reload = reconnect, can request history via protocol)
- No CORS needed (WS from extension host, not browser page)
