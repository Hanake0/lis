# Lis ↔ OC Sidebar Parity Plan

## Context

The OC SessionSidebar (and every component/hook under [clients/vscode/src/webview/components/session/](clients/vscode/src/webview/components/session/)) is **byte-identical** to OC's [packages/ui/src/components/session/](file:///C:/bkp/Dev/openchamber/packages/ui/src/components/session) — verified with `diff -rq`. What's been gutted in Lis is everything *behind* the UI:

- ~35 stores in [stores/](clients/vscode/src/webview/stores/) (most are 3-10 line stubs vs OC's 50-2200 line originals)
- The entire [sync/](clients/vscode/src/webview/sync/) layer (Lis stubs `session-ui-store`, `viewport-store`, `sync-context`; OC has 30+ files totalling ~10k LOC)
- All of [lib/](clients/vscode/src/webview/lib/) — `projectActions`, `projectResolution`, `openchamberEvents`, etc. are 1-line stubs
- The `opencodeClient` (~1757 LOC in OC) that every store calls into for backend data

The user wants:
1. **Lis-native projects**: a DB-backed `Project` entity that aggregates chats, with full OC parity (path + worktrees + git status).
2. **Real implementations** for every stubbed store/sync/lib piece — using OC originals as the source.
3. **Lis backend grows whatever capabilities are needed** to back the sidebar — **we do not replicate OC's backend**. Lis decides the shape of its own services and protocol. (User explicitly: *"we are NOT replicating the backend. We are handling everything and implementing if necessary in our own lis backend."*)
4. **Sidebar sizing investigation** then decision.
5. **First-class extension CLAUDE.md** — the [clients/vscode/](clients/vscode/) project has no CLAUDE.md today; the only project guidance is the .NET-flavoured root [CLAUDE.md](CLAUDE.md). Add one tailored to the extension (TS, esbuild, webview architecture, store layout, etc.).

This is a large, multi-stage effort. The plan below stages it so each checkpoint is shippable.

---

## Architecture decision: Pure HTTP — REST for request/response, SSE for live streams

**Delete `/web/ws` entirely.** The WebSocket frame protocol is replaced by:

- **REST** for every request/response (CRUD, mutations, queries, action triggers).
- **SSE** for everything streamed or pushed (chat token streams, cross-client change notifications, scheduled-task runs).

Both are plain HTTP, both use the same `Authorization: Bearer ...` header, both live under the same `/api/*` origin, both debug through the same DevTools "Network" tab, both pass through proxies without special config. No custom frame schema to maintain, no `request_id` correlation hack, no hand-rolled reconnect — the .NET side just writes to the response stream and the Node-side reader uses the standard `Response.body` reader (or `EventSource` in browsers).

### REST surface on `Lis.Api`

- **Chats**: `GET /api/chats`, `POST /api/chats`, `PATCH /api/chats/{id}`, `DELETE /api/chats/{id}`, `POST /api/chats/{id}/archive`, `POST /api/chats/{id}/pin`, `DELETE /api/chats/{id}/pin`, `GET /api/chats/{id}/messages`, `PATCH /api/chats/{id}/agent`, `PATCH /api/chats/{id}/overrides`, `GET /api/chats/{id}/context`, `POST /api/chats/{id}/messages` (starts a turn → response body is SSE for the token stream), `POST /api/chats/{id}/abort`
- **Projects**: `GET/POST/PATCH/DELETE /api/projects[/{id}]`, `POST /api/projects/{id}/icon`, `POST /api/projects/reorder`, `POST /api/projects/{id}/touch`, `POST /api/chats/{id}/move`
- **Folders**: `GET /api/session-folders?scope=`, `POST/PATCH/DELETE /api/session-folders[/{id}]`, `POST /api/session-folders/{id}/items`, `DELETE /api/session-folders/{id}/items/{chatId}`
- **Agents**: `GET /api/agents`, `GET /api/chats/{id}/agent`, `PATCH /api/chats/{id}/agent`
- **Git**: `GET /api/git/status?path=`, `GET /api/git/branches?path=`, `GET /api/worktrees?project_path=`, `POST /api/worktrees`, `DELETE /api/worktrees`
- **Scheduled tasks**: `GET /api/projects/{id}/scheduled-tasks`, `POST /api/scheduled-tasks`, `PATCH/DELETE /api/scheduled-tasks/{id}`
- **Multi-run**: `POST /api/multi-run`
- **GitHub**: `GET /api/github/status`, `PUT /api/github/token`, `DELETE /api/github/token`, `POST /api/github/pr-status`
- **Permissions / Questions**: `GET /api/permissions/pending`, `POST /api/permissions/{id}/respond`, `GET /api/questions/pending`, `POST /api/questions/{id}/respond`
- **SSE**: `GET /api/chats/{id}/stream` (token stream for a chat's in-flight turn — closes when turn ends or client disconnects), `GET /api/events` (global push channel for all entity-changed events)

### Controller conventions (RickAi-inspired, override [project CLAUDE.md](CLAUDE.md) Controller Conventions)

Capture in the project CLAUDE.md update (Stage 2):

- `[ApiController]`, `[Route("api/resource/{param}")]`, `[Tags("Name")]`. Resource name plural and lowercase (`api/projects`, `api/session-folders`).
- Primary-constructor DI: `public class ProjectsController(IProjectService projects) : ControllerBase`.
- Method per HTTP verb with `[HttpGet/Post/Patch/Delete("…")]`, `[Produces("application/json")]`, and explicit `[ProducesResponseType(StatusCodes.Status…)]` for every status the action can return.
- Return type: `Task<ActionResult<T>>` with concrete `T`. Use `this.Ok(dto)`, `this.NotFound("message")`, `this.BadRequest("message")`, `this.NoContent()`.
- Binding: always explicit — `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`.
- No try/catch in controllers — throw `KeyNotFoundException` (→404), `ArgumentException`/`BadHttpRequestException` (→400), `UnauthorizedAccessException` (→403), `OperationCanceledException` (→499). A central `ApiExceptionFilter` maps them to `ProblemDetails` with the current trace id. Pattern lifted from [RickAi/Api/Filters/ApiExceptionFilter.cs](file:///C:/bkp/Dev/Hanake0/RickAi/Api/Filters/ApiExceptionFilter.cs).
- JSON: snake_case via `JsonOpt.Configure()` (Lis convention — RickAi uses default camel, Lis overrides to snake_case for DTOs that touch the wire; we keep that).
- Auth: `Authorization: Bearer ${WEB_CHANNEL_TOKEN}` enforced by a single middleware on the `api` route group. Same token the WebSocket uses today.
- CORS: allow `vscode-webview://*` origin and any explicit origin from the `lis.serverUrl` setting. Configured in `Program.cs`.
- DTOs live in `Lis.Api/Schemas/{Resource}/` (matches RickAi's `Schemas/{Domain}/` layout, scoped per resource), with `[JsonPropertyName("snake_case")]`, `[Required]`, `[MaxLength]` annotations.

### SSE event schema

Two endpoints, both server→client only, content-type `text/event-stream`:

1. **`GET /api/chats/{id}/stream`** — token stream for one chat's active turn.
   - Events: `message`, `chunk`, `thinking_chunk`, `tool_start`, `tool_result`, `typing`, `typing_stopped`, `permission_request`, `question`, `agent_info`, `context`, `error`, `done`.
   - Each event has `event:` set and `data:` carrying the JSON payload.
   - Connection closes after `done` event or when the client disconnects. Server cleans up resources on disconnect.

2. **`GET /api/events`** — global push channel for cross-client change notifications.
   - Events: `chat_created`, `chat_updated`, `chat_deleted`, `chat_archived`, `chat_moved`, `chat_pinned`, `chat_unpinned`, `project_created`, `project_updated`, `project_deleted`, `folder_created`, `folder_updated`, `folder_deleted`, `git_status_changed`, `scheduled_task_run`, `multi_run_progress`.
   - Server keeps a fan-out registry; every controller action calls `IEventBus.PublishAsync(event)` after the state change.
   - Reconnect handled by client with `Last-Event-ID` header → server replays missed events from an in-memory ring buffer (last N seconds).

### Extension host owns both transports; webview proxies through `postMessage`

The webview is sandboxed (CSP, no cross-origin). The extension host (Node, no sandbox) holds the connection state and proxies:

- **`LisHttpClient`** ([clients/vscode/src/lisHttpClient.ts](clients/vscode/src/lisHttpClient.ts), new) — reads `lis.serverUrl` + `lis.authToken` from config; one `fetch` per request, adds the bearer token.
- **`LisEventStream`** ([clients/vscode/src/lisEventStream.ts](clients/vscode/src/lisEventStream.ts), new) — one long-lived `fetch('/api/events', { headers: { Authorization: ... } })`, reads `Response.body` as an SSE stream (Node has a built-in stream reader; or use the `eventsource` npm package for parsing). Auto-reconnects with `Last-Event-ID`.
- **`LisChatStream`** — per-active-chat SSE reader for `/api/chats/{id}/stream`. Opened on `POST /api/chats/{id}/messages`, closed on `done`.

Webview ↔ extension host envelope types (postMessage):
- `http_request { request_id, method, path, query?, body? }` → reply `http_response { request_id, status, body }`
- `sse_event { source: 'chat' | 'events', chat_id?, event, data }` (extension host pushes all SSE events to the relevant webview)
- `connection_state { http: 'ok' | 'unauthorised' | 'unreachable', events: 'connected' | 'disconnected' }` (status-bar feedback)

URL/token are read **only in the extension host**. Webview never sees them. `workspace.onDidChangeConfiguration('lis')` re-inits the HTTP client and reconnects the event stream.

### Webview-side adapter

[lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts) (new — `LisRpcClient`) exposes OC's `OpencodeService` method signatures so dropped-in OC stores compile unchanged. Each method becomes one `http_request` postMessage and awaits the matching `http_response`. SSE events bubble up via the same `window.addEventListener('message', ...)` path as today but tagged `sse_event`.

### Config shape

Change [package.json](clients/vscode/package.json):
- `lis.serverUrl` becomes a **base origin** (e.g. `http://localhost:3010` or `https://lis.example.com`). Default `http://localhost:3010` (configurable; the only "default localhost" anywhere). Description updated.
- Extension host derives `${serverUrl}/api` for REST and `${serverUrl}/api/events` + `${serverUrl}/api/chats/{id}/stream` for SSE. No more `ws://`.
- `lis.authToken` unchanged.
- **Migration**: if `lis.serverUrl` matches the old `ws://...` shape, strip `ws://` → `http://` and drop any `/web/ws` path. Log a deprecation warning. One-shot in `activate()`.

---

## Stage 0 — Sidebar sizing investigation ✅ DONE

### Measurements (via Playwright DevTools eval)

| Surface              | html font | label rendered | row height |
|----------------------|-----------|----------------|------------|
| OC desktop (browser default) | 16px      | 14px           | ~24px      |
| VS Code Explorer reference   | 13px      | 13px           | 22px       |
| Lis sidebar (before) | **13px**  | **11.375px**   | **17px**   |
| Lis sidebar (after)  | **16px**  | **14px**       | **24px**   |

### Decision

OC tokens (`--text-ui-label`, `--text-meta`, etc.) are `rem`-based and designed against a **16px html base**. We were inheriting `var(--vscode-font-size, 13px)` on `html`, which collapsed every rem to ~76% of intent. The fix is a single CSS rule applied only when the sidebar webview opts in:

```css
html.lis-sidebar { font-size: 16px; }
```

Body font stays at `var(--vscode-font-size, 13px)` because the existing `body { font-size: ... }` rule wins for body. So plain inherited text (chat messages, VS Code-style chrome) renders at VS Code's 13px, but rem-based OC tokens hit their designed 14/15/12/11px scale.

### Implementation

- [styles.css](clients/vscode/src/webview/styles.css): the one rule above. No `[data-lis-sidebar]` selectors, no per-token overrides.
- [sidebarOC.tsx](clients/vscode/src/webview/sidebarOC.tsx): `installSidebarFrameHandler` adds the `lis-sidebar` class onto `html` alongside `dark`.

---

## Stage 1 — Connection config UX (no hardcoded localhost beyond a default)

The extension already exposes `lis.serverUrl` and `lis.authToken` as VS Code settings ([package.json:62-77](clients/vscode/package.json)) and reads them in [extension.ts:67-73](clients/vscode/src/extension.ts). The defaults are `ws://localhost:3010/web/ws` and empty string. **No new hardcoded URLs anywhere in new code** — the webview-side `LisRpcClient` (Stage 9) only postMessages to the extension host, which owns the URL/token and both transports (HTTP + SSE).

What this stage adds:

### First-run notification + open settings

In [extension.ts](clients/vscode/src/extension.ts) `activate()`:
- After `readConfig()`, if `cfg.url` is empty OR `cfg.token` is empty, do **not** auto-connect. Instead `vscode.window.showInformationMessage('Lis is not configured', 'Open Settings', 'Connect anyway')`. On `Open Settings` → `vscode.commands.executeCommand('workbench.action.openSettings', '@ext:lis.lis-vscode')`. On `Connect anyway` → fall through to `client.connect()`.
- Persist a `lis.firstRunDismissed` global state flag so we don't nag on every reload — but always re-show if config is still empty after a manual reload.

### Status bar item

Add a single `StatusBarItem` (alignment left, priority low):
- Text: `$(circle-filled) Lis` (green icon = connected, yellow = connecting, red = disconnected/error).
- Tooltip: shows the current `serverUrl` and connection state.
- Command: a new `lis.configure` that opens settings filtered to `@ext:lis.lis-vscode`.
- Created in `activate()`, disposed in `deactivate()`. State updated from `LisHttpClient` health checks + `LisEventStream` connection events (both emit `connection_state` to subscribers).

### New command

`lis.configure` → opens VS Code settings filtered to the Lis section. Register in [extension.ts](clients/vscode/src/extension.ts), declare in [package.json contributes.commands](clients/vscode/package.json).

### Verify no other hardcoded localhost

Grep `clients/vscode/src/` and `Lis.Api/` for `localhost`, `127.0.0.1`, `3010`, `ws://`. Only allowed matches: the **default value** of `lis.serverUrl` in `package.json` and the `.env.example` defaults for `WEB_CHANNEL_PORT`. Any others (test fixtures, dev URLs in code) must be replaced with config-reading.

---

## Stage 2 — Extension CLAUDE.md + project CLAUDE.md update + dead-code cleanup

### Project root [CLAUDE.md](CLAUDE.md) — extend "Controller Conventions" with RickAi-derived rules

Replace the current short Controller Conventions section with:

- `[ApiController]`, `[Route("api/resource/{param}")]`, `[Tags("Name")]`. Resource name plural and lowercase.
- Primary-constructor DI: `public class ProjectsController(IProjectService projects) : ControllerBase`.
- Per-verb method with `[HttpGet/Post/Patch/Delete("…")]`, `[Produces("application/json")]`, explicit `[ProducesResponseType(StatusCodes.Status…)]` per status the action returns.
- Return type: `Task<ActionResult<T>>` with concrete `T`. Use `this.Ok(dto)`, `this.NotFound("msg")`, `this.BadRequest("msg")`, `this.NoContent()`.
- Always explicit binding: `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`.
- **No try/catch in controllers.** Throw `KeyNotFoundException` (→404), `ArgumentException`/`BadHttpRequestException` (→400), `UnauthorizedAccessException` (→403), `OperationCanceledException` (→499). A central `Lis.Api/Filters/ApiExceptionFilter.cs` (port from [RickAi/Api/Filters/ApiExceptionFilter.cs](file:///C:/bkp/Dev/Hanake0/RickAi/Api/Filters/ApiExceptionFilter.cs)) maps them to `ProblemDetails` with the active trace id.
- JSON: snake_case via `JsonOpt.Configure()` (Lis convention; we override RickAi's default camel because every channel uses snake_case).
- DTOs in `Lis.Api/Schemas/{Resource}/` with `[JsonPropertyName]`, `[Required]`, `[MaxLength]`.
- Auth: `Authorization: Bearer ${WEB_CHANNEL_TOKEN}` enforced by a single middleware on the `api` route group.
- CORS: allow `vscode-webview://*` and any origin matching configured `lis.serverUrl`. Configured in `Program.cs`.

Also add a "SSE Conventions" subsection:
- One event type per kind; `event:` line always set; `data:` carries snake_case JSON; `id:` set for replayable events.
- Controllers expose SSE via `IAsyncEnumerable<ServerSentEvent>` (.NET 10's built-in) or direct `Response.Body` writes with `Content-Type: text/event-stream` and `X-Accel-Buffering: no`.
- `Last-Event-ID` header is honoured by `EventsController` — server replays from in-memory ring buffer.

### Extension CLAUDE.md ([clients/vscode/CLAUDE.md](clients/vscode/CLAUDE.md), new)

Required sections:
- **Project Overview** — VS Code extension that hosts a React webview chat UI (full App) and a sidebar webview view (OC-style session sidebar). Talks to Lis backend over plain HTTP — REST for actions, SSE for streams.
- **Commands** — `bun run build`, `bun run watch`, `bun run typecheck`, `bun pm install`, `bunx --bun @vscode/vsce package`, F5 in VS Code launches Extension Development Host.
- **Architecture**
  - Extension host: [extension.ts](clients/vscode/src/extension.ts) wires [LisHttpClient](clients/vscode/src/lisHttpClient.ts), [LisEventStream](clients/vscode/src/lisEventStream.ts), [LisChatStream](clients/vscode/src/lisChatStream.ts), [sidebarWebviewProvider.ts](clients/vscode/src/sidebarWebviewProvider.ts), [chatPanel.ts](clients/vscode/src/chatPanel.ts). The host owns all network state; webviews are dumb renderers proxied through postMessage.
  - Webview ([webview/](clients/vscode/src/webview/)): React 19 + Tailwind 4 + Zustand. Entry [index.ts](clients/vscode/src/webview/index.ts) branches on `__LIS_SIDEBAR_MODE` to render either [SidebarOCApp](clients/vscode/src/webview/sidebarOC.tsx) or full [App](clients/vscode/src/webview/app.tsx).
  - Shared bundle: `dist/chat.js` is used by both modes. (`dist/sidebar.js` is legacy and is removed in the dead-code cleanup below.)
- **Protocol — pure HTTP: REST + SSE**
  - REST endpoints live under `{lis.serverUrl}/api/*` on `Lis.Api`. Controllers in [Lis.Api/Controllers/](Lis.Api/Controllers/).
  - SSE for live streams: `GET /api/chats/{id}/stream` per active turn, `GET /api/events` for global cross-client pushes. Event names listed in the SSE schema section above.
  - **No WebSocket.** `Lis.Channels/Web/WebChannelController.cs` is replaced by REST controllers and the SSE endpoints in Stage 13.
  - Webview never makes a raw HTTP request. It postMessages `{ type: 'http_request', request_id, method, path, body? }` to the extension host. Extension host owns `LisHttpClient` and replies with `{ type: 'http_response', request_id, status, body }`. SSE events bubble back as `{ type: 'sse_event', source, event, data }`.
  - Webview-side adapter: [lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts) (`LisRpcClient`) exposes OC `OpencodeService` method signatures so OC stores compile unchanged.
- **Webview stores layout** — [stores/](clients/vscode/src/webview/stores/) (UI state), [sync/](clients/vscode/src/webview/sync/) (server-synced state). Today most are stubs — OC originals live at `openchamber/packages/ui/src/stores/` and `openchamber/packages/ui/src/sync/`.
- **OC component reuse** — [components/session/](clients/vscode/src/webview/components/session/) is byte-identical to OC. Do not modify these files; if a behaviour change is needed, fix the underlying store. Verify identity with `diff -rq` against OC.
- **Build pipeline** — Tailwind compiles [styles.css](clients/vscode/src/webview/styles.css) → `dist/chat.css`. esbuild bundles `src/extension.ts` (CJS, `vscode` external), `src/webview/index.ts` (IIFE, browser), `src/webview/sidebar.ts` (legacy IIFE — slated for removal). Aliases: `@/` → `src/webview/`. SDK stubs at `lib/{opencode-sdk-stub,tauri-stub,pierre-stub}.ts`.
- **Two webview render targets**
  - **Chat panel** (`vscode.WebviewPanel` opened on demand by [ChatPanelManager](clients/vscode/src/chatPanel.ts)) — renders [App](clients/vscode/src/webview/app.tsx).
  - **Sidebar view** (`vscode.WebviewView` registered as `lis.sidebar`) — renders [SidebarOCApp](clients/vscode/src/webview/sidebarOC.tsx) with the OC SessionSidebar.
- **Testing the extension** — F5 from VS Code launches Extension Development Host. Or `bunx --bun @vscode/vsce package` → install VSIX into a regular VS Code. There is no Playwright harness for VS Code itself; for visual checks we serve [test-sidebar.html](clients/vscode/test-sidebar.html) with `python -m http.server` and inspect through Playwright MCP.
- **Configuration** — `lis.serverUrl` (base origin, default `http://localhost:3010`), `lis.authToken`, `lis.verboseLogging`. Read in [extension.ts:67-73](clients/vscode/src/extension.ts) and re-read on `workspace.onDidChangeConfiguration('lis')`.
- **Activity bar contribution** — `lis-sidebar` container with a single `lis.sidebar` webview view. Manifested in [package.json:18-39](clients/vscode/package.json).
- **CSS variable conventions** — VS Code injects `--vscode-*` into webviews. We re-export them as design-system tokens in [styles.css:15-83](clients/vscode/src/webview/styles.css). For the sidebar specifically, root uses `--vscode-sideBar-background` (not editor bg) — see [sidebarOC.tsx:46-52](clients/vscode/src/webview/sidebarOC.tsx).
- **Adding a new operation — REST checklist**
  1. Add a controller method under `Lis.Api/Controllers/` (snake_case JSON, `ProblemDetails` errors, `[ProducesResponseType]`).
  2. Add a method on `LisRpcClient` in [webview/lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts).
  3. Consume from the appropriate store under [stores/](clients/vscode/src/webview/stores/) or [sync/](clients/vscode/src/webview/sync/).
- **Adding a new push event — SSE checklist**
  1. Define the event name + payload DTO in `Lis.Api/Schemas/Events/`.
  2. Publish from the relevant service via `IEventBus.PublishAsync(new MyEvent(...))` after the state change.
  3. Add the discriminated-union variant to [src/types.ts](clients/vscode/src/types.ts) `SseEvent`.
  4. Extension host's `LisEventStream` already forwards everything to webviews; no host code change.
  5. Handle in `sync/sync-context.tsx` (or the relevant store) to update local state.
- **Code style** — TS strict mode, tabs, single quotes, no semicolons in JSX, named exports. Follow OC's style for ported files (it's prevalent in `components/session/` already).
- **Gotchas**
  - Webview reloads on extension reload — sidebar state is lost. Persistent state goes through Lis backend, not webview-only storage.
  - CSP forbids `connect-src` to arbitrary hosts. Webview cannot fetch external URLs — every request and SSE subscription routes through the extension host via postMessage.
  - `rem` units in tokens resolve against html font-size; html font-size is set to `var(--vscode-font-size, 13px)`. Changing this scales the whole UI.

### Dead-code cleanup (after CLAUDE.md so it documents the right reality)

- Delete [src/sessionsTreeProvider.ts](clients/vscode/src/sessionsTreeProvider.ts) — unused, was the pre-OC native tree view.
- Delete [src/webview/sidebar.ts](clients/vscode/src/webview/sidebar.ts), [sidebarApp.tsx](clients/vscode/src/webview/sidebarApp.tsx), [sidebarStore.ts](clients/vscode/src/webview/sidebarStore.ts) — legacy lightweight sidebar bundle, fully superseded by `sidebarOC.tsx` rendered through the main `chat.js` bundle.
- Remove the `sidebarConfig` entry from [esbuild.mjs:71-82](clients/vscode/esbuild.mjs).
- Verify nothing in [test-sidebar.html](clients/vscode/test-sidebar.html) still references `dist/sidebar.js` (we already updated it).
- Fix the bug at [extension.ts:62](clients/vscode/src/extension.ts) — `sessions = null` references a variable that no longer exists; will throw `ReferenceError` on deactivation. Remove the line.

---

## Stage 3 — Lis backend: Project entity

**Goal**: persist projects in Postgres, expose CRUD via REST + SSE change events.

### Entity ([Lis.Persistence/Entities/ProjectEntity.cs](Lis.Persistence/Entities/ProjectEntity.cs), new)

Fields (semantic mirror of OC's [`ProjectEntry`](file:///C:/bkp/Dev/openchamber/packages/ui/src/lib/api/types.ts) at lines 563-578, named our way):
- `id` (long PK), `external_id` (varchar(64), unique — generated from path hash; clients refer to this)
- `path` (varchar(1024)), `label` (varchar(256), nullable)
- `icon` (varchar(64), nullable — emoji or icon-name)
- `icon_image_mime` (varchar(64), nullable), `icon_image_blob` (bytea, nullable), `icon_image_source` (varchar(8), 'custom' or 'auto'), `icon_image_updated_at` (timestamptz)
- `icon_background` (varchar(16), hex)
- `color` (varchar(32), nullable — token name from `PROJECT_COLORS`)
- `added_at`, `last_opened_at`, `created_at`, `updated_at` (timestamptz)
- `sidebar_collapsed` (bool), `sort_index` (int)
- Nav: `ICollection<ChatEntity> Chats`

### ChatEntity change

Add `[Column("project_id")] long? ProjectId` + nav `Project?`. FK with `OnDelete(SetNull)`. Add index.

### Migration

```bash
dotnet ef migrations add add_projects \
  --project Lis.Persistence/Lis.Persistence.csproj \
  --startup-project Lis.Api/Lis.Api.csproj
```

### Service ([Lis.Persistence/Services/ProjectService.cs](Lis.Persistence/Services/ProjectService.cs), new — interface + impl in same file per project convention)

`IProjectService` methods:
- `ListAsync()`, `GetByExternalIdAsync(ext)`
- `CreateAsync(path, label?, color?)` — auto-derives external_id (path hash), label (path basename), color (least-used from palette)
- `UpdateMetaAsync(ext, { label?, color?, icon?, iconBackground? })`
- `SetIconImageAsync(ext, mime, blob, source)`, `RemoveIconAsync(ext)`
- `DeleteAsync(ext)` — chats remain with `project_id = NULL`
- `ReorderAsync(orderedExternalIds[])`
- `TouchLastOpenedAsync(ext)`
- `MoveChatToProjectAsync(chatExternalId, projectExternalId?)`

Add `[Trace("ProjectService > MethodName")]` to every async method per [CLAUDE.md tracing conventions](CLAUDE.md).

### REST controller ([Lis.Api/Controllers/ProjectsController.cs](Lis.Api/Controllers/ProjectsController.cs), new)

- `GET /api/projects` → `Project[]`
- `POST /api/projects` body `{ path, label?, color? }` → `Project`
- `GET /api/projects/{externalId}` → `Project`
- `PATCH /api/projects/{externalId}` body `{ label?, color?, icon?, iconBackground?, sidebarCollapsed? }` → `Project`
- `DELETE /api/projects/{externalId}`
- `POST /api/projects/reorder` body `{ orderedExternalIds: string[] }`
- `POST /api/projects/{externalId}/icon` (multipart `mime`, `blob`, `source`) → `Project`
- `DELETE /api/projects/{externalId}/icon`
- `POST /api/projects/{externalId}/touch` (updates `last_opened_at`)
- `POST /api/chats/{chatExternalId}/move` body `{ projectExternalId?: string | null }`

### SSE events

After any mutation, the controller calls `IEventBus.PublishAsync(new ProjectCreatedEvent(...))` (or `Updated`/`Deleted`/`ChatMovedEvent`). All connected `/api/events` subscribers receive the event.

Event DTOs in `Lis.Api/Schemas/Events/{Project,Chat}Events.cs`. Snake_case, same `[JsonPropertyName]` style as REST DTOs.

### Mirror in webview types

Extend [clients/vscode/src/types.ts](clients/vscode/src/types.ts) with the new `SseEvent` variants and a `Project` interface. Add `project_id` to `ChatSummary`.

**Out of scope here**: directory binding & worktree discovery — Stage 5.

---

## Stage 4 — Lis backend: Session folders + Pins

OC's [`SessionFolder`](file:///C:/bkp/Dev/openchamber/packages/ui/src/stores/useSessionFoldersStore.ts) is a per-scope (directory or `__archived__:dir`) tree of folders containing session IDs.

### Entities

- `SessionFolderEntity` (id, external_id, scope_key (varchar(512)), name, parent_external_id?, sort_index, created_at, updated_at)
- `SessionFolderItemEntity` (folder_id FK, chat_external_id, sort_index) — many-to-many
- `pinned_at` column on `ChatEntity` (timestamptz, nullable)

### Service + REST + SSE

`ISessionFolderService` methods: `ListForScopeAsync`, `CreateAsync`, `RenameAsync`, `DeleteAsync`, `AddSessionsAsync`, `RemoveSessionAsync`, `CleanupAsync`. `IChatPinService.PinAsync` / `UnpinAsync`.

REST: `GET /api/session-folders?scope=`, `POST /api/session-folders`, `PATCH /api/session-folders/{id}`, `DELETE /api/session-folders/{id}`, `POST /api/session-folders/{id}/items` (`{ chatIds }`), `DELETE /api/session-folders/{id}/items/{chatId}`, `POST /api/chats/{id}/pin`, `DELETE /api/chats/{id}/pin`.

SSE events on `/api/events`: `folder_created`, `folder_updated`, `folder_deleted`, `chat_pinned`, `chat_unpinned`.

---

## Stage 5 — Lis backend: Git + Worktrees

### Library

**LibGit2Sharp** (pure .NET, no shell-out, cross-platform). Add to a new `Lis.Git` project or to `Lis.Persistence` (it's not really persistence, but it's a service layer; new project is cleaner).

### Service

`IGitService`:
- `GetRepoStatusAsync(path)` → `{ isGitRepo, branch, ahead, behind, dirty, lastCommit }`
- `ListBranchesAsync(path)` → `{ local: string[], remote: string[], current: string }`
- `ListWorktreesAsync(projectPath)` → `WorktreeMetadata[]` (`{ path, branch, head, isLocked, isPrunable }`)
- `CreateWorktreeAsync(projectPath, newPath, branch)`
- `RemoveWorktreeAsync(projectPath, worktreePath, force)`

### REST + SSE

REST as listed above. SSE event `git_status_changed { path }` on `/api/events`, emitted when a `FileSystemWatcher` on `.git/HEAD` and `.git/index` fires for any project's path.

---

## Stage 6 — Lis backend: Scheduled Tasks

### Library

OC uses `cron-parser`. .NET: **Cronos** (`Cronos.CronExpression.GetNextOccurrence(DateTime utcNow)`).

### Entity

`ScheduledTaskEntity` (id, external_id, project_id FK, name, cron_expr, prompt, agent_name, model, enabled (bool), status, next_run_at (timestamptz, indexed), last_run_at, last_status, last_error, created_at, updated_at).

`next_run_at` is computed and persisted on insert / update / fire — lets the scheduler resync without re-parsing every cron expression. Indexed so the startup query is `ORDER BY next_run_at LIMIT 100` style if needed.

### Min-heap scheduler — no polling

`Lis.Api/Services/ScheduledTaskRunner.cs` — `BackgroundService`. Lis is deployed on **Neon serverless** (scales to zero, billed by compute time). A 30-second polling loop would keep the DB awake permanently. Instead:

1. **On startup**: one query — `SELECT * FROM scheduled_task WHERE enabled = true`. Load into a `SortedSet<(DateTimeOffset nextRun, long taskId)>` (min-heap by `nextRun`).
2. **Main loop**:
   ```csharp
   while (!ct.IsCancellationRequested) {
     var next = heap.Min; // null if empty
     var wait = next is null
       ? Timeout.InfiniteTimeSpan
       : next.Value.nextRun - DateTimeOffset.UtcNow;
     if (wait > TimeSpan.Zero) await Task.Delay(wait, linkedCts.Token); // cancels on heap change
     await FireDueTasksAsync(ct);
   }
   ```
3. **On fire** (`FireDueTasksAsync`): pop all entries whose `nextRun <= now`, for each:
   - Open a scoped `DbContext`, load the task by id (to avoid stale config), inject prompt via `IConversationService`.
   - Update `last_run_at`, `last_status`, `last_error`, and `next_run_at = CronExpression.Parse(cron).GetNextOccurrence(DateTimeOffset.UtcNow)`.
   - Publish `scheduled_task_run { task_id, status, error? }` via `IEventBus`.
   - Push the task back onto the heap with the new `nextRun`.
4. **On CRUD** (via `IEventBus` subscription to `scheduled_task_created/updated/deleted`):
   - Refresh that task in the heap (add / remove / replace).
   - Trigger the linked `CancellationTokenSource` so the `Task.Delay` returns early and the loop recomputes the next wait.
5. **DB touches**: one query at startup, one per CRUD event, one per fire (to write `last_run_at` and read fresh config). Zero polling. Idle = the `Task.Delay` is parked forever; Neon sleeps; we hold no connection.

### REST + SSE

`GET /api/projects/{id}/scheduled-tasks`, `POST /api/scheduled-tasks`, `PATCH /api/scheduled-tasks/{id}`, `DELETE /api/scheduled-tasks/{id}`. The controller writes the row + computes `next_run_at` then publishes `scheduled_task_changed`; the scheduler picks it up via the event bus. SSE event `scheduled_task_run { task_id, status, error? }` on `/api/events` when the scheduler fires a task.

### Edge cases handled

- **Missed runs while the API was down**: on startup, any task with `next_run_at <= now` is fired immediately (one catch-up per task — we don't replay every missed slot). Document this behaviour; users wanting backfill must use a different tool.
- **Cron returns null** (expression that never fires again, e.g. a past one-shot): the task is set `enabled = false` and removed from the heap.
- **Clock skew / time-zone**: store cron expressions as UTC. The DTO accepts an optional `timezone` field and the scheduler converts using `TimeZoneInfo`.
- **Concurrent fires**: if multiple tasks share `nextRun`, fire them sequentially in the same `FireDueTasksAsync` pass to avoid stampedes on the LLM provider.

---

## Stage 7 — Lis backend: Multi-run launcher

### REST + SSE

`POST /api/multi-run` body `{ prompt, targets: [{ session_id, agent?, model? }] }` → `{ run_id }`. Server enqueues a message to each target via `IConversationService`. Emits SSE `multi_run_progress { run_id, session_id, status, error? }` on `/api/events` for each target.

No new entity — runs are ephemeral.

---

## Stage 8 — Lis backend: GitHub integration

Two-step:

### 8a — Identity (now)

Store user-supplied PAT in `GitHubIdentityEntity` (user_id long? for future multi-user, login, token_encrypted (use `IDataProtectionProvider`), scopes, added_at). REST: `GET /api/github/status`, `PUT /api/github/token`, `DELETE /api/github/token`. Octokit.net for API calls.

### 8b — PR status (deferrable)

REST `POST /api/github/pr-status` body `{ keys: [{ owner, repo, branch }] }` → `{ results: { key: { state, checks, reviews } } }`. Cache 60s in-memory.

If 8b is deferred at delivery time, the store can keep its stub (returning empty results) and the UI degrades gracefully.

---

## Stage 9 — REST + SSE adapter + sync foundation

### Extension host: `LisHttpClient` ([clients/vscode/src/lisHttpClient.ts](clients/vscode/src/lisHttpClient.ts), new)

- Constructor reads `lis.serverUrl` (base origin) + `lis.authToken` from config.
- `request<T>(method, path, options?: { query?, body?, signal? })` returns `{ status, body }`. Uses Node `fetch` (available in VS Code's Node runtime).
- Always sets `Authorization: Bearer ${token}`, `Content-Type: application/json` for body, `Accept: application/json`.
- Throws only on network errors; HTTP errors return the status + body for the webview to handle.
- Re-init on `workspace.onDidChangeConfiguration('lis')`.

### Extension host: `LisEventStream` ([clients/vscode/src/lisEventStream.ts](clients/vscode/src/lisEventStream.ts), new)

- One long-lived `fetch('${serverUrl}/api/events', { headers: { Authorization: ... }, signal })`. Reads `Response.body` as an async stream and parses SSE frames (or uses the [`eventsource`](https://www.npmjs.com/package/eventsource) npm package which handles parsing + reconnect).
- Emits `{ event, data, id }` to subscribers. Tracks `Last-Event-ID` for reconnect.
- Auto-reconnect with exponential backoff (1s → 30s cap). Emits `connection_state` events on transitions.
- Stop / restart on config change.

### Extension host: `LisChatStream` ([clients/vscode/src/lisChatStream.ts](clients/vscode/src/lisChatStream.ts), new)

- Per-chat SSE reader for `/api/chats/{id}/stream`. Opened when the webview hits `POST /api/chats/{id}/messages`; closed on `done` event or `AbortController`.
- Same parser as `LisEventStream`.

### Extension host: proxy in webview providers

Both [sidebarWebviewProvider.ts](clients/vscode/src/sidebarWebviewProvider.ts) and [chatPanel.ts](clients/vscode/src/chatPanel.ts) add three message handlers:

```ts
case 'http_request': {
  const { request_id, method, path, query, body } = msg;
  const { status, body: respBody } = await httpClient.request(method, path, { query, body });
  view.webview.postMessage({ type: 'http_response', request_id, status, body: respBody });
  // If POST /api/chats/{id}/messages succeeded, open a LisChatStream for that chat and proxy its events.
  return;
}
case 'open_chat_stream': {
  chatStreams.open(msg.chat_id, (event) =>
    view.webview.postMessage({ type: 'sse_event', source: 'chat', chat_id: msg.chat_id, event: event.event, data: event.data }));
  return;
}
case 'close_chat_stream': {
  chatStreams.close(msg.chat_id);
  return;
}
```

`LisEventStream` events are forwarded to every connected webview as `{ type: 'sse_event', source: 'events', event, data }`.

### Webview: `lib/lis/rpc-client.ts` (new)

`LisRpcClient` exposes OC's `OpencodeService` method signatures so dropped-in OC stores compile unchanged. Internals:

```ts
async listProjects(): Promise<Project[]> { return this.req('GET', '/projects'); }
async createSessionFolder(scope: string, name: string, parentId?: string): Promise<SessionFolder> {
  return this.req('POST', '/session-folders', { body: { scope, name, parent_id: parentId } });
}
private req<T>(method, path, opts?): Promise<T> {
  const request_id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    this.pending.set(request_id, { resolve, reject });
    vscode.postMessage({ type: 'http_request', request_id, method, path, ...opts });
  });
}
```

A single top-level message listener in [sidebarOC.tsx](clients/vscode/src/webview/sidebarOC.tsx) `installSidebarFrameHandler` (and the chat-panel equivalent):

```ts
case 'http_response':
  LisRpcClient.instance.settle(msg.request_id, msg.status, msg.body);
  return;
case 'sse_event':
  EventBus.emit(msg.source, msg.event, msg.data);
  return;
```

Methods to implement (derived from grep across OC stores):
- Projects: `listProjects`, `createProject`, `updateProject`, `deleteProject`, `reorderProjects`, `setProjectIcon`, `removeProjectIcon`, `moveChat`, `touchProject`
- Folders: `listSessionFolders`, `createSessionFolder`, `renameSessionFolder`, `deleteSessionFolder`, `moveSessionToFolder`, `removeSessionFromFolder`, `pinChat`, `unpinChat`
- Chats: `listChats`, `createChat`, `getChat`, `updateChat`, `deleteChat`, `archiveChat`, `getChatMessages`, `setChatAgent`, `saveChatOverrides`, `getChatContext`, `sendChatMessage` (also opens the chat SSE stream), `abortChat`
- Agents: `listAgents`
- Git: `getRepoStatus`, `listBranches`, `listWorktrees`, `createWorktree`, `removeWorktree`
- Scheduled: `listScheduledTasks`, `createScheduledTask`, `updateScheduledTask`, `deleteScheduledTask`
- Multi-run: `runMultiRun`
- GitHub: `setGitHubToken`, `clearGitHubToken`, `getGitHubStatus`, `getPrStatus`
- Permissions/questions: `listPendingPermissions`, `listPendingQuestions`, `respondToPermission`, `respondToQuestion`

### `sync/sync-context.tsx` (port OC original, ~2200 LOC)

- Bootstrap via `LisRpcClient.X()` (signatures match OC).
- Subscribe to SSE: OC originally consumed SSE — same model. Subscribe to the `EventBus` channels `'events'` (global) and `'chat'` (per-chat). The handlers for `project_*`, `folder_*`, `chat_*`, `git_status_changed`, `scheduled_task_run`, `multi_run_progress` update the relevant stores. Token-stream events (`chunk`, `thinking_chunk`, etc.) update the active session's message buffer.

### `sync/session-ui-store.ts` (port OC original, ~1223 LOC)

Replace our 116-line stub. All mutating actions delegate to `LisRpcClient`. The existing WS-frame `send_frame` postMessage path is removed.

### `sync/viewport-store.ts` (port OC original)

Replace our 12-line stub.

### Other sync files

Port wholesale: `event-pipeline`, `event-reducer`, `live-aggregate`, `session-cache`, `notification-store`, `selection-store`, `materialization`, `optimistic`, `streaming`, `reconnect-recovery`, `submit`, `voice-store`, etc. Most are pure logic and need no adaptation.

---

## Stage 10 — Webview: replace every stub store with OC original

For each file in [stores/](clients/vscode/src/webview/stores/) with a >10-line OC counterpart:

1. Copy OC original (`openchamber/packages/ui/src/stores/{name}.ts`) over the Lis stub.
2. Imports already line up (Lis uses the same `@/` alias).
3. If the store calls `opencodeClient.X()`, ensure that method exists on `LisRpcClient` (Stage 9).
4. Where a store uses electron/desktop-only APIs, guard with `isVSCodeRuntime() ? null : ...` — most already do.
5. Verify by triggering the store's actions from a sidebar dialog.

Priority (driven by SessionSidebar imports):
- `useProjectsStore` (726 LOC)
- `useSessionFoldersStore` (554 LOC)
- `useSessionPinnedStore` (54 LOC)
- `useSessionMultiSelectStore` (157 LOC)
- `useGlobalSessionsStore` (349 LOC)
- `useGitStore` (1077 LOC)
- `useGitHubAuthStore` (71), `useGitHubPrStatusStore` (727), `useGitIdentitiesStore` (285)
- `useMultiRunStore` (285)
- `useUpdateStore` (308) — point at the VS Code marketplace update API or noop
- Remaining ~20 stores

**Special**: [useUIStore.ts](clients/vscode/src/webview/stores/useUIStore.ts) — OC's is 2163 LOC, we have Lis-specific overrides (sidebar toggle dispatches a VS Code command at [useUIStore.ts:30-40](clients/vscode/src/webview/stores/useUIStore.ts)). Merge OC's UIStore but preserve our overrides.

---

## Stage 11 — Webview: replace every stub lib helper with OC original

For each file in [lib/](clients/vscode/src/webview/lib/) that's a stub (>1 line in OC):

- `projectActions.ts`, `projectResolution.ts`, `projectMeta.ts`
- `openchamberEvents.ts`, `openchamberConfig.ts`
- `desktop.ts`, `desktopHosts.ts`, `desktopNative.ts`, `desktopSsh.ts`
- `gitApi.ts`, `worktrees/worktreeManager.ts`, `worktrees/worktreeStatus.ts`
- `persistence.ts`, `directoryPersistence.ts`
- `api/types.ts` (port the type definitions, then re-export from Lis API surface where they overlap)
- All stubbed `hooks/`

Mechanical port. Skip electron-only code paths with `isVSCodeRuntime()` guards.

---

## Stage 12 — Extension host wiring

### [sidebarWebviewProvider.ts](clients/vscode/src/sidebarWebviewProvider.ts) + [chatPanel.ts](clients/vscode/src/chatPanel.ts)

- Handle `http_request` → delegate to `LisHttpClient`, reply `http_response`.
- Handle `open_chat_stream` / `close_chat_stream` → delegate to `LisChatStream`.
- Forward all `LisEventStream` events to every webview as `sse_event { source: 'events', ... }`.
- Forward all `LisChatStream` events to the target webview as `sse_event { source: 'chat', chat_id, ... }`.
- Remove the old `send_frame` / WS-frame handlers — they're replaced by REST.

### Bootstrap on activation

`activate()` constructs `LisHttpClient`, `LisEventStream` (auto-connects), and an idle `LisChatStream` registry. Webviews are constructed lazily as views resolve. The webview bootstraps its own data via `LisRpcClient` calls from `sync-context.tsx` on mount.

### `LisClient` (existing WebSocket client) — delete

Once Stage 13 lands, [clients/vscode/src/lisClient.ts](clients/vscode/src/lisClient.ts) is deleted along with its imports across the extension host.

---

## Stage 13 — Delete WebSocket entirely; migrate chat/agent to REST + SSE

The existing [`WebChannelController.cs`](Lis.Channels/Web/WebChannelController.cs) (646 LOC) handles every chat operation over a custom WebSocket frame protocol. It is replaced entirely by:

- `Lis.Api/Controllers/ChatsController.cs` (new) — chat CRUD + messages
- `Lis.Api/Controllers/AgentsController.cs` (new) — agent listing + assignment
- `Lis.Api/Controllers/PermissionsController.cs` (new) — permission responses
- `Lis.Api/Controllers/QuestionsController.cs` (new) — question responses
- `Lis.Api/Controllers/EventsController.cs` (new) — global SSE `/api/events` endpoint
- `Lis.Api/Controllers/ChatStreamController.cs` (new) — per-chat SSE `/api/chats/{id}/stream`

### Endpoints to add (matching existing WS handler behaviour)

- `GET /api/chats` (was `list_chats`)
- `POST /api/chats` (was `create_session` — body `{ chatId, name? }`)
- `PATCH /api/chats/{id}` (was `rename_session` — body `{ name }`)
- `DELETE /api/chats/{id}` (was `delete_session`)
- `POST /api/chats/{id}/archive` (was `archive_session`)
- `GET /api/chats/{id}/messages?limit=` (was `history`)
- `POST /api/chats/{id}/messages` (was `message` — body matches today's frame; emits SSE events on `/api/chats/{id}/stream`)
- `POST /api/chats/{id}/abort` (was `abort`)
- `GET /api/agents` (was `list_agents`)
- `GET /api/chats/{id}/agent` (was `get_agent`)
- `PATCH /api/chats/{id}/agent` (was `set_agent` — body `{ agent_name }`)
- `PATCH /api/chats/{id}/overrides` (was `save_overrides` — body `{ model?, effort?, tool_profile? }`)
- `GET /api/chats/{id}/context?force=` (was `get_context`)
- `POST /api/permissions/{approvalId}/respond` (was `permission_response` — body `{ decision }`)
- `POST /api/questions/{questionId}/respond` (was `question_response` — body `{ answers?, rejected? }`)

### EventBus

`IEventBus` (new, in `Lis.Core`): `Subscribe(handler)` / `Unsubscribe(handler)` / `PublishAsync(event)`. In-memory fan-out, optionally backed by a `Channel<T>` ring buffer of size N for `Last-Event-ID` replay. `EventsController` opens an SSE response and subscribes for the lifetime of the request. `ChatStreamController` does the same but filtered to one chat id.

### Delete

- `Lis.Channels/Web/WebChannelController.cs`
- `Lis.Channels/Web/WebChannelClient.cs`
- `Lis.Channels/Web/Schemas/{ServerFrame,ClientFrame}.cs`
- `Lis.Channels/Web/WebChannelSetup.cs` (replaced by the EventBus + new controllers in `Lis.Api/Program.cs`)
- The whole `Lis.Channels/Web` directory can be deleted if nothing else lives in it
- `clients/vscode/src/lisClient.ts`
- `clients/vscode/src/types.ts` — collapse the `ServerFrame`/`ClientFrame` unions into `SseEvent` only

### Update

- [extension.ts:36-39](clients/vscode/src/extension.ts) `lis.newChat` — use `httpClient.request('POST', '/chats', { body: {...} })` then `panels.openOrFocus(chatId)`.
- `IConversationService` — methods that previously broadcast WS frames now `IEventBus.PublishAsync(...)`.

### Ordering inside Stage 13

Two PRs to keep the extension working mid-migration:
1. Add the new chat/agent/permission/question controllers + SSE endpoints. Wire `IConversationService` to publish via `IEventBus`. Migrate `LisRpcClient` chat methods to REST + SSE. The WS controller stays compiled but unused. Verify end-to-end (existing chats still work via WS, new ones via REST — they share the same DB so it doesn't matter).
2. Delete every WS file (`WebChannelController`, `WebChannelClient`, `WebChannelSetup`, the schemas, `LisClient`). Update `Program.cs` to remove WS registration. Single big cleanup commit.

---

## Stage 14 — Verification

Manual checklist in Extension Development Host:

1. **Project**: create with path picker, edit name/color/icon, delete, reorder, sticky header.
2. **Chat ↔ Project**: open VS Code in a project folder, create chat, see it under the right project. Move chat between projects via context menu.
3. **Folders**: create folder, drag session into it, rename, delete (sessions detach).
4. **Pins**: pin/unpin a chat, see it bubble to top.
5. **Search**: type query, see groups filter.
6. **Bulk**: enable selection mode, multi-select chats, bulk archive/delete.
7. **Worktrees**: in a git repo project, create a new worktree, see it appear; open chat in it.
8. **Git status**: switch branch in repo, sidebar updates branch label live.
9. **Scheduled tasks**: create task with `*/2 * * * *` cron, observe it fire in 2 min, status updates.
10. **Multi-run**: launch prompt to 2 sessions, both receive it, both stream.
11. **GitHub**: connect PAT, see PR badges on session groups whose branch has an open PR.
12. **Sizing**: visual diff vs VS Code Explorer.

---

## Out of scope (explicit non-goals)

- **Electron auto-update** — confirmed skip by user.
- **Tunnels / Cloudflare** — OC-server-only feature.
- **Voice / TTS** — not requested for Lis.
- **Preview server / package manager** — OC dev-server features.
- **Magic prompts** — defer; stub returning empty.
- **Skills catalog** — defer; stub returning empty.

---

## Magnitude warning

This plan covers **~15-25k lines** of porting work plus the Lis backend buildout (new REST surface across ~10 controllers, the SSE EventBus, services, migrations, and the WebSocket teardown). Stages 1-3 alone (connection UX + CLAUDE.md + cleanup + projects backend + webview wiring) is a multi-day task. Each subsequent stage is independent and shippable.

Stage 13 (deleting the WebSocket transport entirely) is intentionally last — Stages 3-8 build the new REST + SSE surface for the *new* features (projects, folders, git, scheduled tasks, multi-run, GitHub). Those stages publish events through the EventBus from day one, so by the time we get to Stage 13 the SSE pipeline is already proven in production. Stage 13 then ports the *existing* chat/agent endpoints over and deletes the WS layer in one final sweep.

Recommend executing in stage order and pausing for review at the end of each stage so we can re-prioritise if needed.

---

## Files modified / created (representative — not exhaustive)

**Documentation**
- [clients/vscode/CLAUDE.md](clients/vscode/CLAUDE.md) (new)

**Connection UX (Stage 1)**
- [clients/vscode/src/extension.ts](clients/vscode/src/extension.ts) — first-run notification, status bar item, `lis.configure` command, `lis.serverUrl` migration shim
- [clients/vscode/package.json](clients/vscode/package.json) — declare `lis.configure` command, change `lis.serverUrl` default to `http://localhost:3010` + update description

**REST + SSE adapter (Stage 9)**
- [clients/vscode/src/lisHttpClient.ts](clients/vscode/src/lisHttpClient.ts) (new) — extension-host HTTP client reading config
- [clients/vscode/src/lisEventStream.ts](clients/vscode/src/lisEventStream.ts) (new) — long-lived `/api/events` SSE reader with `Last-Event-ID` reconnect
- [clients/vscode/src/lisChatStream.ts](clients/vscode/src/lisChatStream.ts) (new) — per-chat SSE reader for `/api/chats/{id}/stream`
- [clients/vscode/src/webview/lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts) (new) — webview RPC adapter (postMessage-based)
- [clients/vscode/src/sidebarWebviewProvider.ts](clients/vscode/src/sidebarWebviewProvider.ts), [chatPanel.ts](clients/vscode/src/chatPanel.ts) — proxy handlers for `http_request`, `open_chat_stream`, `close_chat_stream`; relay `sse_event`

**Delete WS entirely (Stage 13)**
- Delete `Lis.Channels/Web/WebChannelController.cs`, `WebChannelClient.cs`, `WebChannelSetup.cs`, `Schemas/{ServerFrame,ClientFrame}.cs`. The whole `Lis.Channels/Web` directory may go away.
- Delete `clients/vscode/src/lisClient.ts`
- Collapse [clients/vscode/src/types.ts](clients/vscode/src/types.ts) `ServerFrame`/`ClientFrame` unions into `SseEvent`
- New `Lis.Api/Controllers/{Chats,Agents,Permissions,Questions,Events,ChatStream}Controller.cs`
- New `Lis.Core/IEventBus.cs` + impl
- Update [extension.ts:36-39](clients/vscode/src/extension.ts) `lis.newChat` to call `httpClient.request('POST', '/chats', ...)`

**Dead-code removal**
- Delete `clients/vscode/src/sessionsTreeProvider.ts`, `src/webview/sidebar.ts`, `src/webview/sidebarApp.tsx`, `src/webview/sidebarStore.ts`
- [esbuild.mjs](clients/vscode/esbuild.mjs) — remove `sidebarConfig`
- [extension.ts:62](clients/vscode/src/extension.ts) — remove undeclared `sessions = null`

**Backend (new/modified)**
- `Lis.Persistence/Entities/ProjectEntity.cs` (new), `ChatEntity.cs` (add project_id, pinned_at)
- `Lis.Persistence/Entities/SessionFolderEntity.cs`, `SessionFolderItemEntity.cs`
- `Lis.Persistence/Entities/ScheduledTaskEntity.cs`, `GitHubIdentityEntity.cs`
- `Lis.Persistence/Services/{ProjectService,SessionFolderService,ChatPinService,ScheduledTaskService,GitHubService}.cs` (new)
- `Lis.Git/Lis.Git.csproj` (new — LibGit2Sharp wrapper) with `IGitService`
- `Lis.Api/Services/ScheduledTaskRunner.cs` (new HostedService)
- `Lis.Api/Schemas/Chats/ChatSummary.cs` (add `project_id`, `pinned_at` fields)
- `Lis.Api/Schemas/Events/{Project,Chat,Folder,Git,ScheduledTask,MultiRun}Events.cs` (new event DTOs)
- `Lis.Api/Controllers/{Projects,SessionFolders,Worktrees,Git,ScheduledTasks,MultiRun,GitHub,Chats,Agents,Permissions,Questions,Events,ChatStream}Controller.cs` — full REST + SSE surface (replacing `Lis.Channels/Web/WebChannelController.cs`)
- New migrations under `Lis.Persistence/Migrations/`

**Webview (port from OC, keep imports unchanged)**
- `clients/vscode/src/webview/stores/*.ts` (port ~25 files from `openchamber/packages/ui/src/stores/`)
- `clients/vscode/src/webview/sync/*.ts` (port ~30 files from `openchamber/packages/ui/src/sync/`)
- `clients/vscode/src/webview/lib/**/*.ts` (port stubs from `openchamber/packages/ui/src/lib/`)
- `clients/vscode/src/webview/lib/lis/rpc-client.ts` (new — Lis-flavoured RPC adapter exposing OC's `OpencodeService` signatures)
- `clients/vscode/src/webview/sidebarOC.tsx` (remove synthetic-project hack; data flows from real `useProjectsStore`)

**Extension host**
- `clients/vscode/src/sidebarWebviewProvider.ts`, `chatPanel.ts`, `lisClient.ts`, `extension.ts`
- `clients/vscode/src/types.ts` (extend frame unions to match new backend schemas)

**Styling (Stage 0)**
- `clients/vscode/src/webview/styles.css`, `styles/design-system.css`
