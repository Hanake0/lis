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

| Setting              | Default                            | What it does                                      |
| -------------------- | ---------------------------------- | ------------------------------------------------- |
| `lis.serverUrl`      | `ws://localhost:3010/web/ws`       | WebSocket URL of the Lis server.                  |
| `lis.authToken`      | `""`                               | Must match `WEB_CHANNEL_TOKEN` on the server.     |
| `lis.verboseLogging` | `false`                            | Log every WS frame to the Lis output channel.     |

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
│ Webview (src/webview/*.ts)       │
│  • streaming renderer            │
│  • tool call blocks              │
│  • message metadata              │
│  • composer controls             │
│  • bundled by esbuild → dist/    │
└────────────┬─────────────────────┘
             │ postMessage
┌────────────▼─────────────────────┐
│ ChatPanelManager                 │
│  • one WebviewPanel per chat     │
│  • routes frames by chat_id      │
└────────────┬─────────────────────┘
             │ send / onFrame
┌────────────▼─────────────────────┐
│ LisClient                        │
│  • WebSocket wrapper             │
│  • reconnect with backoff        │
└──────────────────────────────────┘
   SessionsTreeProvider
   • sidebar TreeView of chats
   • subscribes to chat_list frames
```

* `extension.ts` creates Logger, LisClient, SessionsTreeProvider, ChatPanelManager. Registers commands and wires config change listener.
* `src/types.ts` — shared frame type definitions (ServerFrame, ClientFrame, DTOs).
* `src/lisClient.ts` — pure transport: connect, send, fire frame/state listeners. Reconnects with capped exponential backoff (1s → 30s).
* `src/chatPanel.ts` — `ChatPanelManager` owns one `WebviewPanel` per chat_id. Filters incoming frames by chat_id and forwards to the matching panel.
* `src/sessionsTreeProvider.ts` — native TreeView sidebar listing chats sorted by recency.
* `src/webview/` — webview TypeScript modules bundled by esbuild into `dist/chat.js`.
* `media/chat.css` — webview styles.
* Two esbuild entry points: `src/extension.ts` (extension host) and `src/webview/index.ts` (webview bundle).

## Limitations

* Plain text only — no markdown rendering yet (the body is shown as `pre-wrap`).
* No media uploads.

## Manual Test

1. Set `WEB_CHANNEL_ENABLED=true` and `WEB_CHANNEL_TOKEN=test123` in `.env`, plus `ANTHROPIC_ENABLED=true`.
2. Run `dotnet run --project Lis.Api` (or `docker compose up`).
3. In VS Code settings, set `lis.authToken` to `test123` and `lis.serverUrl` to `ws://localhost:3010/web/ws`.
4. Open the Lis sidebar, click `+`, type "hello", press Enter.
5. You should see the user message appear, a typing indicator, and then the assistant's reply.
