# Lis VS Code Extension

The Lis extension is a web extension (no Node APIs) that connects to the
[Web Channel](./WEB_CHANNEL.md) over WebSocket. It runs in desktop VS Code,
vscode.dev, and code-server.

## Install

The extension lives at `clients/vscode/`. To use it from source:

```bash
cd clients/vscode
npm install
npm run build       # writes dist/extension.js
```

Then load it in VS Code:

* **Desktop**: open the folder in VS Code, press `F5` to launch an Extension Development Host.
* **Web**: run `vsce package --pre-release` and side-load the `.vsix`, or symlink the folder under `~/.vscode/extensions/`.

## Configure

In VS Code Settings → Extensions → Lis:

| Setting          | Default                            | What it does                                      |
| ---------------- | ---------------------------------- | ------------------------------------------------- |
| `lis.serverUrl`  | `ws://localhost:3010/web/ws`       | WebSocket URL of the Lis server.                  |
| `lis.authToken`  | `""`                               | Must match `WEB_CHANNEL_TOKEN` on the server.     |

Changes are picked up live — the extension reconnects automatically.

## Usage

* Open the Lis view from the activity bar.
* `+` mints a new chat (`web:<uuid>`). The server creates the row on the first message.
* Enter sends; Shift+Enter inserts a newline.
* Connection status (`connecting` / `connected` / `disconnected`) is shown at the top.

Commands (Cmd/Ctrl+Shift+P):

* **Lis: Connect** — open the socket.
* **Lis: Disconnect** — close the socket; auto-reconnect stops.
* **Lis: New Chat** — same as the `+` button.

## Architecture

```
┌──────────────────────────────────┐
│ Webview (chat.html/css/js)       │
│  • renders messages, typing      │
│  • postMessage → extension       │
└────────────┬─────────────────────┘
             │ postMessage
┌────────────▼─────────────────────┐
│ ChatViewProvider                 │
│  • owns chat list + current chat │
│  • bridges webview ↔ LisClient   │
└────────────┬─────────────────────┘
             │ send / onFrame
┌────────────▼─────────────────────┐
│ LisClient                        │
│  • WebSocket wrapper             │
│  • reconnect with backoff        │
└──────────────────────────────────┘
```

* `extension.ts` activates the provider, registers commands, and forwards config changes.
* `lisClient.ts` is pure transport: connect, send, fire frame/state listeners. Reconnects with capped exponential backoff (1s → 2s → … → 30s).
* `chatViewProvider.ts` is the bridge — owns chat list state, posts frames to the webview, and translates webview events back to client frames.
* `media/chat.{html,css,js}` is the UI. The HTML template is inlined as a string in the provider so the bundled extension is self-contained.

## Limitations (First Cut)

* Plain text only — no markdown rendering yet (the body is shown as `pre-wrap`).
* No tool-call cards.
* No streaming — assistant replies arrive as complete messages.
* No media uploads.
* No optimistic local rendering — the user message renders only after the server echoes it back (so other clients connected to the same Lis instance see it too).

## Manual Test

1. Set `WEB_CHANNEL_ENABLED=true` and `WEB_CHANNEL_TOKEN=test123` in `.env`, plus `ANTHROPIC_ENABLED=true`.
2. Run `dotnet run --project Lis.Api` (or `docker compose up`).
3. In VS Code settings, set `lis.authToken` to `test123` and `lis.serverUrl` to `ws://localhost:3010/web/ws`.
4. Open the Lis sidebar, click `+`, type "hello", press Enter.
5. You should see the user message appear, a typing indicator, and then the assistant's reply.
