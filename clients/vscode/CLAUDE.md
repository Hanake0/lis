# Lis VS Code Extension

VS Code extension that hosts a React webview chat UI (full panel) and an OC-style session sidebar (webview view). Talks to the Lis backend over plain HTTP — REST for requests, SSE for live streams.

The parent project's [../../CLAUDE.md](../../CLAUDE.md) covers .NET backend conventions. **This file owns everything under `clients/vscode/`.**

## Commands

```bash
# Install deps (always use bun — node has native-build issues here)
bun install

# Build (Tailwind + esbuild, both bundles)
bun run build

# Watch (for development)
bun run watch

# Type-check (does not build)
bun run typecheck

# Unit tests (bun's built-in runner — no extra deps)
bun test

# Package a VSIX for installation
bunx --bun @vscode/vsce package --no-dependencies --allow-missing-repository

# Launch Extension Development Host (interactive)
# Open this folder in VS Code, press F5.
```

## Architecture

Two halves, both built by [esbuild.mjs](esbuild.mjs):

1. **Extension host** (Node, runs inside VS Code's extension process). Entry [src/extension.ts](src/extension.ts). Owns network state — HTTP client, SSE subscription, status bar, command registration.
2. **Webview** (browser, sandboxed). Entry [src/webview/index.ts](src/webview/index.ts). React 19 + Tailwind 4 + Zustand. Renders one of two roots:
   - **Full chat panel** ([App](src/webview/app.tsx)) — opened on demand by [ChatPanelManager](src/chatPanel.ts) as a `vscode.WebviewPanel`.
   - **Session sidebar** ([SidebarOCApp](src/webview/sidebarOC.tsx)) — registered as the `lis.sidebar` `vscode.WebviewView`. The branch is chosen via `window.__LIS_SIDEBAR_MODE` set by [SidebarWebviewProvider](src/sidebarWebviewProvider.ts).

Both modes share the same `dist/chat.js` bundle. The `dist/sidebar.js` bundle is legacy and slated for removal.

## Protocol — REST + SSE (no WebSocket)

- **REST**: `{lis.serverUrl}/api/*` on the Lis backend. Controllers in [../../Lis.Api/Controllers/](../../Lis.Api/Controllers/). Stage 9+ migrates everything here.
- **SSE**: `GET /api/chats/{id}/stream` per active chat turn (token stream), `GET /api/events` for global push (entity-changed broadcasts).
- **No WebSocket.** During the migration window (until Stage 13) the legacy `/web/ws` is still wired through `LisClient` — but new features go through `LisHttpClient` / `LisEventStream` / `LisChatStream`.

### Webview never makes raw HTTP

CSP forbids `connect-src` to arbitrary hosts. The webview sandbox can't fetch external URLs. Every request and SSE subscription goes through the extension host:

- Webview → host: `vscode.postMessage({ type: 'http_request', request_id, method, path, body? })`
- Host → webview: `postMessage({ type: 'http_response', request_id, status, body })` plus `{ type: 'sse_event', source, event, data }` for pushes.
- Webview-side adapter ([src/webview/lib/lis/rpc-client.ts](src/webview/lib/lis/rpc-client.ts), Stage 9) exposes OC's `OpencodeService` method signatures so OC stores compile unchanged.

## Configuration

User-facing settings (declared in [package.json](package.json), read in [src/extension.ts](src/extension.ts) via [src/config.ts](src/config.ts)):

| Setting               | Type    | Default                    | Purpose                                                              |
|-----------------------|---------|----------------------------|----------------------------------------------------------------------|
| `lis.serverUrl`       | string  | `http://localhost:3010`    | Base origin. REST/SSE/(legacy WS) URLs are derived from this.        |
| `lis.authToken`       | string  | `""`                       | Shared secret matching `WEB_CHANNEL_TOKEN` on the server.            |
| `lis.verboseLogging`  | boolean | `false`                    | Log every protocol frame to the Lis output channel.                  |

- The only "default localhost" anywhere is the `lis.serverUrl` default in `package.json`. No hardcoded URLs anywhere in code.
- Legacy `ws://…/web/ws` values are auto-migrated to the canonical base origin on activation. See `normalizeBaseOrigin` in [src/config.ts](src/config.ts).
- `workspace.onDidChangeConfiguration('lis')` triggers a re-init of the HTTP client and event stream — no reload needed.

## First-run UX

On activation, if `lis.authToken` is empty and the user hasn't dismissed the notification before, a one-shot info notification appears with an "Open Settings" button (routed via the `lis.configure` command). State stored in `context.globalState` under `lis.firstRunNotificationDismissed`.

## Status bar

[src/statusBar.ts](src/statusBar.ts) — single left-aligned `StatusBarItem`:
- `$(plug) Lis` — connected
- `$(sync~spin) Lis` — connecting
- `$(debug-disconnect) Lis` — disconnected (with `statusBarItem.warningBackground`)

Click → `lis.configure` → opens settings filtered to `@ext:lis.lis-vscode`.

## Webview store layout

| Path                                 | What lives here                                                                |
|--------------------------------------|--------------------------------------------------------------------------------|
| [src/webview/stores/](src/webview/stores/) | Domain stores (projects, folders, sessions, git, etc.). Mostly Zustand.    |
| [src/webview/sync/](src/webview/sync/)     | Server-synced state — driven by SSE events from `/api/events`.             |
| [src/webview/components/session/](src/webview/components/session/) | **Byte-identical** to OC's [packages/ui/src/components/session/](file:///C:/bkp/Dev/openchamber/packages/ui/src/components/session). Don't modify directly — fix the underlying store. Verify identity with `diff -rq`. |

## Adding work

**New REST operation** — checklist:
1. Add the controller method under `Lis.Api/Controllers/` (see root [../../CLAUDE.md](../../CLAUDE.md) Controller Conventions).
2. Add a typed method on `LisRpcClient` in [src/webview/lib/lis/rpc-client.ts](src/webview/lib/lis/rpc-client.ts) with the OC `OpencodeService` signature.
3. Consume from the relevant store under [src/webview/stores/](src/webview/stores/) or [src/webview/sync/](src/webview/sync/).

**New SSE event** — checklist:
1. Define the DTO in `Lis.Api/Schemas/Events/`.
2. Publish from the relevant service via `IEventBus.PublishAsync(new MyEvent(...))` after state mutation.
3. Add the variant to [src/types.ts](src/types.ts) `SseEvent` union.
4. Handle in `src/webview/sync/sync-context.tsx` (or the relevant store) to update local state. Extension-host plumbing already forwards everything to webviews — no host code change needed.

## CSS variable conventions

VS Code injects all `--vscode-*` design tokens into webviews. We re-export them as OC design-system tokens in [src/webview/styles.css](src/webview/styles.css) `:root`. The sidebar applies a `html.lis-sidebar { font-size: 16px }` rule so rem-based OC tokens render at their 16px design base (see Stage 0 of [../../Plans/oc-sidebar-parity.md](../../Plans/oc-sidebar-parity.md)).

For the sidebar specifically the root uses `--vscode-sideBar-background` (not editor bg) — see [src/webview/sidebarOC.tsx](src/webview/sidebarOC.tsx).

## Code style

- TypeScript strict mode, tabs, single quotes, no semicolons in JSX where Prettier would drop them.
- Named exports. Default exports only for React component files where the convention demands it.
- OC-style for files under `components/session/` (they're byte-identical to OC originals; preserve formatting).

## Testing

- **Unit tests**: [bun test](https://bun.sh/docs/cli/test). Co-locate `*.test.ts` next to source. Pure-logic only (config parsing, URL helpers, store reducers).
- **Component / DOM tests**: `bun test` with happy-dom (add when needed). Not set up yet.
- **Visual / sidebar snapshots**: serve `test-sidebar.html` via `python -m http.server 8765` and drive with Playwright. Compares rendered sidebar against real chat data flowing through `__LIS_SIDEBAR_MODE`.
- **Full extension E2E**: install the VSIX into a real VS Code (or launch Extension Development Host via F5). No automated runner yet — `@vscode/test-electron` can be added if Stage 14 needs it.

## Activity bar contribution

`lis-sidebar` container declared in [package.json](package.json) with a single `lis.sidebar` webview view. Icon at [media/icon.svg](media/icon.svg). View provider registered in [src/extension.ts](src/extension.ts).

## Gotchas

- **Reloading the extension wipes webview state.** Anything persistent goes through the Lis backend, not webview-only storage.
- **CSP forbids `connect-src` to arbitrary hosts.** Every request and SSE subscription routes through the extension host via postMessage.
- **`rem` units resolve against `html` font-size.** We set body to `var(--vscode-font-size, 13px)` so VS Code chrome stays consistent, but the sidebar overrides html to 16px so OC tokens render at their design size.
- **The `dist/sidebar.js` bundle is legacy.** All sidebar code now lives in `dist/chat.js` behind the `__LIS_SIDEBAR_MODE` flag. The bundle will be removed once tests confirm nothing else references it.

## See also

- [../../CLAUDE.md](../../CLAUDE.md) — root project conventions (.NET backend, controllers, services, DB).
- [../../Plans/oc-sidebar-parity.md](../../Plans/oc-sidebar-parity.md) — the OC sidebar parity plan this extension is being rebuilt against.
- [NOTICE.md](NOTICE.md) — third-party notices (OC component reuse, MIT).
