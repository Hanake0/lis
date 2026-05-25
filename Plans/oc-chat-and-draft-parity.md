# OC Chat + Draft Flow Parity

## Context

The OC sidebar parity work ([oc-sidebar-parity.md](oc-sidebar-parity.md)) brought the projects/sessions list to 1-1 behavioural parity. What's still divergent is everything *inside* the chat panel — the composer chrome, the new-chat flow, the project-add picker, and archived-session navigation. This plan closes those gaps.

User intent (verbatim):

- **"we need to have 1-1 parity on behaviour on the chat tab"** (UX) — **"we improve code for the lis-backend where possible"** (data layer)
- **"on our impl, we are able to open many chat tabs and work in parallel in many projects. that way we dont need to set or depend on vscodes workspace folder. so, we set open project/worktree per chat-tab."**

That second point is the architectural fork from OC: each Lis chat panel owns its own `(project, worktree)` binding which the composer's top-row pills edit live. Tools that run inside a chat (read file, bash, git) use that binding as the cwd. VS Code's `workspace.workspaceFolders` is treated as a soft hint at draft-creation time only.

Concrete divergences observed today (paths relative to repo root):

1. **Archived sessions don't open on click.** The Lis intercept at [sidebarOC.tsx:31-48](clients/vscode/src/webview/sidebarOC.tsx) wraps `setCurrentSession(id)` with one argument; OC's signature is `(id, directory)` ([OC useSessionActions.ts:95](file:///C:/bkp/Dev/openchamber/packages/ui/src/components/session/sidebar/hooks/useSessionActions.ts)). The wrapper drops `directory`, and the host's `openOrFocus` flow can no-op for archived rows. Repro: click any row under the "archived" group.

2. **Add-Project dialog renders constrained to the sidebar webview's ~485px width.** The component itself ([DirectoryExplorerDialog.tsx](clients/vscode/src/webview/components/session/DirectoryExplorerDialog.tsx)) is **byte-identical** to OC (750 LOC each, `diff -q` matches) — the cramp is purely a function of the host surface. VS Code's webview sandbox can't escape its container, so opening the picker from inside the sidebar will always look constrained no matter the CSS. Default path also opens at `~/` (Windows home) instead of a useful seed.

3. **Composer is missing the model picker, effort selector, thinking-mode toggle, provider icons, and per-chat project/worktree pills.** Root causes:
   - [`useModelLists`](clients/vscode/src/webview/hooks/useModelLists.ts) is a 3-line stub returning the wrong shape; [OC's version](file:///C:/bkp/Dev/openchamber/packages/ui/src/hooks/useModelLists.ts) returns `{ favoriteModelsList, recentModelsList }` of `ProviderWithModelList`-typed entries.
   - [`useConfigStore`](clients/vscode/src/webview/stores/useConfigStore.ts) is 154 LOC vs OC's 2266; missing `providers`, `defaultProviders`, `modelsMetadata`, `loadProviders`. ModelControls (byte-identical to OC, 2820 LOC) reads these — empty → "Not selected".
   - [`contextStore`](clients/vscode/src/webview/stores/contextStore.ts) is a 3-line stub vs OC 511; ModelControls reads it for the context-window breakdown — empty → budget UI hides.
   - [`useProviderLogo`](clients/vscode/src/webview/hooks/useProviderLogo.ts) is 2 LOC vs OC 98; no provider SVGs ship → no logos.
   - [`ChatInput.tsx:3302`](clients/vscode/src/webview/components/chat/ChatInput.tsx) gates `showDraftTargetSelectors` with `newSessionDraftOpen && !isVSCode` — OC intentionally hides them in VS Code on the assumption the host fixes the project context. For our multi-chat-per-panel model this gate is wrong; we want them visible *whenever the chat is editable*, not just during draft.

4. **"+ New session" eagerly POSTs `/api/chats` and opens a real row.** [extension.ts:59-71](clients/vscode/src/extension.ts) creates the row via REST before any user action. OC creates a *draft* (no server row) and only persists on first send.

5. **Per-chat worktree binding has no storage.** [ChatEntity](Lis.Persistence/Entities/ChatEntity.cs) has `ProjectId` but no `Directory` column, so the user can't pin a chat to a specific worktree under that project. Tools run against whatever the server decides — there's no per-chat cwd.

6. **Send button is permanently disabled in the chat panel.** Root cause: the chat panel webview ([app.tsx:21-29](clients/vscode/src/webview/app.tsx)) sets `useChatStore.chatId` on receiving the host's `init` envelope but **never sets `useSessionUIStore.currentSessionId`**. OC's `ChatInput` send button is gated on `!currentSessionId && !newSessionDraftOpen` ([OC ChatInput.tsx:707](file:///C:/bkp/Dev/openchamber/packages/ui/src/components/chat/ChatInput.tsx)) — with both falsy, the button stays disabled and no one can send. The legacy `useChatStore` is a separate world the OC component doesn't read from.

This is Stage 15 in the broader OC parity effort.

---

## Architecture decisions

### Per-chat `(project, worktree)` binding lives on `ChatEntity`

Add a nullable `directory: varchar(1024)` column on `ChatEntity`. Semantics:

- `chat.project_id` — already exists; the *project* this chat belongs to (sidebar grouping, project-scoped folders, etc.).
- `chat.directory` — the chat's effective working directory. Null means "use the project root". Non-null means a specific worktree path (must be under `project.path` or a worktree of it).
- Tools (Read, Bash, Git, Worktree) running inside a chat use `chat.directory ?? chat.project.path` as cwd.
- Migrations: one new column, indexed only if grouping by it later proves expensive (skip the index for now).

Each chat panel is independent. Two panels open against different projects → they each operate against their own cwd. There is no implicit VS Code workspace folder dependence; the host-side `vscode.workspace.workspaceFolders` is only used as a *suggestion* when minting a new draft and the sidebar has no active project (fallback chain in §15d.3).

### Draft chat lifecycle — pre-mint id, lazy server-create on send

The webview mints a draft chatId client-side (`web:draft-${uuid}`), opens a `vscode.WebviewPanel` in **draft mode**, and only on the first `POST /api/chats/{id}/messages` does the server materialise the row. The bindings (`project_id`, `directory`) the user picked in the composer's pills flow through the same POST so the row lands bound on creation.

Why pre-mint + lazy-create:
- Matches OC's UX: one chat container handles both draft and active state seamlessly ([OC ChatContainer.tsx:522](file:///C:/bkp/Dev/openchamber/packages/ui/src/components/chat/ChatContainer.tsx)).
- No panel disposal / re-creation on send — preserves composer focus, attached files, scroll.
- Server contract stays REST-idempotent: extend `ChatsController.SendMessage` so that when `{id}` doesn't exist it creates the row with `(project_id, directory)` from the request envelope and continues. Retry with same id is idempotent.

### Backend: `GET /api/providers` for model metadata

ModelControls + `useConfigStore.loadProviders` need a provider+model registry. Today Lis registers providers via env (`ANTHROPIC_ENABLED=true`, see [Program.cs](Lis.Api/Program.cs)) with no enumeration surface. Add:

- `GET /api/providers` → `{ providers: ProviderDto[], default: { model: string, agent: string } }`.
- `ProviderDto` per provider: `id` (`"anthropic"`), `name` (`"Anthropic"`), `models: Record<string, ModelDto>` mirroring OC's `Provider["models"]` SDK shape (id, name, release_date, attachment.{image,video,audio} bools, reasoning + supported_efforts, cost.{input,output,cache_read,cache_write}, limit.{context,output}, experimental).
- Implementation reads `IProviderMetadata` services registered by each `Add*` extension method. Anthropic ships a static catalog of `claude-sonnet-4-6`, `claude-opus-4-7`, `claude-haiku-4-5-20251001` matching [CLAUDE.md](CLAUDE.md). Cached for the process lifetime.
- Mirror snake_case JSON via `JsonOpt`. Follows Lis [Controller Conventions](CLAUDE.md) — `[ApiController] [Route("api/providers")]`, primary-ctor DI, snake_case, no try/catch, `[Trace]` on the async method.

This keeps the wire shape compatible with OC's `Provider` SDK type so the byte-identical `ModelControls` works without per-render adaptation.

### Add-Project picker: modal-feel WebviewPanel (backdrop + centered card)

**Constraint:** VS Code extensions cannot overlay the workbench chrome (sidebar, activity bar, panel). The picker can render either inside the sidebar `WebviewView` (~485px, cramped) or inside an editor-area `WebviewPanel`. We use the editor-area panel **but** paint a full-area semi-opaque backdrop with the OC dialog as a small centered card on top — visually a modal hovering over the editor area, technically a tab that paints itself like one.

- New host command `lis.openAddProject` opens a `WebviewPanel` (column = Active, retainContextWhenHidden = false, singleton — focus existing if open).
- The picker webview's React entry renders:
  - A `fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center` backdrop covering the entire panel surface.
  - On top, a centered card (~720×560, capped to viewport-32px on each side) hosting `DirectoryExplorerDialog`. The dialog component stays byte-identical to OC — we just don't wrap it in VS Code's `<Dialog>` (which has its own positioning). Use OC's `MobileOverlayPanel` for the inner card chrome since it already implements the centered overlay we want.
  - Clicking the backdrop or pressing Escape posts `{ type: 'close_add_project' }` to host → host disposes the panel.
- Sidebar's `+ Add project` button posts `{ type: 'open_add_project' }` → host runs `lis.openAddProject`.
- On add: picker posts `{ type: 'add_project_committed', path, label?, color? }` → host calls existing `POST /api/projects` → disposes the panel.
- Default path on open: active sidebar project's path > most-recently-opened project > `~`. No dependence on `workspace.workspaceFolders`.

Visually this is the closest possible to OC's modal inside VS Code's API. The backdrop hides the editor underneath; the sidebar/activity bar/panel chrome remain visible on the side, which matches how modals work in any tabbed UI host. If the user later wants the modal to overlay the sidebar/activity bar too, that requires a separate Electron shell — out of scope.

### Composer chrome — restore pills for every chat, not just drafts

In Lis, each chat panel can edit its own `(project, worktree)` binding any time (multi-chat parallel). So the OC-only "draft mode" gate is wrong for us:

- Replace `showDraftTargetSelectors = newSessionDraftOpen && !isVSCode` with `showProjectWorktreePills = true` (always show in the composer for both draft and persisted chats; clicking either pill opens a project/worktree picker dropdown).
- For a persisted chat, picking a new project triggers `PATCH /api/chats/{id}` updating `project_id`. Picking a worktree triggers `PATCH /api/chats/{id}` updating `directory`. Both publish `chat_updated` via `IEventBus` so the sidebar re-buckets.
- For a draft chat, the pills only mutate `useSessionUIStore.newSessionDraft.{selectedProjectId, directoryOverride}`; they're sent to the server on first message.

---

## Phase 15a — Delete the legacy chat surface; wire the OC ChatContainer cleanly

The chat panel today carries TWO chat UIs glued together: the OC `ChatContainer` (byte-identical port, mounted via `MainLayout → ChatView`) AND a dead Lis-legacy stack (`components/composer/`, `components/messages/`, `components/MessageList.tsx`, `components/StatusBar.tsx`, `components/ContextPanel.tsx`, `MainLayout.tsx`'s sidebar/right-rail wrappers, `webview/store.ts`'s `useChatStore`). The legacy stack still owns `chatId` and `composerModel/Effort/Profile/Thinking` via `useChatStore`; OC's `ChatInput` reads `useSessionUIStore.currentSessionId` (never set in the chat panel today) so the send button is permanently disabled. That's the message-send bug.

There is no fallback to keep. Delete the legacy surface in full and wire the OC ChatContainer directly.

### 15a.1 Delete legacy chat code

Files to delete:
- [clients/vscode/src/webview/store.ts](clients/vscode/src/webview/store.ts) (`useChatStore`)
- [clients/vscode/src/webview/components/composer/](clients/vscode/src/webview/components/composer/) (entire dir: `Composer.tsx`, `AgentPicker.tsx`, `EffortPicker.tsx`, `ModelInput.tsx`, `ProfilePicker.tsx`, `ThinkingModePicker.tsx`)
- [clients/vscode/src/webview/components/messages/MessageHeader.tsx](clients/vscode/src/webview/components/messages/MessageHeader.tsx) and the rest of `components/messages/` if nothing else there is referenced
- [clients/vscode/src/webview/components/MessageList.tsx](clients/vscode/src/webview/components/MessageList.tsx)
- [clients/vscode/src/webview/components/StatusBar.tsx](clients/vscode/src/webview/components/StatusBar.tsx)
- [clients/vscode/src/webview/components/ContextPanel.tsx](clients/vscode/src/webview/components/ContextPanel.tsx)
- [clients/vscode/src/webview/components/layout/MainLayout.tsx](clients/vscode/src/webview/components/layout/MainLayout.tsx) **only if** `ChatView` is the sole intended export — confirmed via `grep`: no other file imports the legacy components listed above outside this cluster. MainLayout's `Header`, `Sidebar`, `RightSidebar`, etc. are also unused by the chat panel under the new architecture (the chat panel is just `ChatContainer`).
- [clients/vscode/src/webview/hooks/useSessionActivity.ts](clients/vscode/src/webview/hooks/useSessionActivity.ts) (10 LOC stub depending on `useChatStore`; the OC original is 70 LOC — port if anything else consumes it, otherwise delete)

After each delete, run `bun run typecheck` to surface any references; resolve them by deleting the offending consumer or rewriting to the OC equivalent.

### 15a.2 Rewrite `app.tsx` to render ChatContainer directly

[clients/vscode/src/webview/app.tsx](clients/vscode/src/webview/app.tsx) becomes minimal:

```tsx
export function App() {
    return (
        <I18nProvider>
            <ErrorBoundary>
                <TooltipProvider delayDuration={300} skipDelayDuration={150}>
                    <div className="h-full w-full text-foreground bg-background dark">
                        <ChatErrorBoundary>
                            <ChatContainer />
                        </ChatErrorBoundary>
                        <Toaster />
                        <SessionDialogs />
                    </div>
                </TooltipProvider>
            </ErrorBoundary>
        </I18nProvider>
    );
}
```

No `useChatStore`. No `MainLayout`. The `init` envelope handling moves into the chat-panel-bridge (next step).

### 15a.3 Chat-panel-bridge sets `currentSessionId` on init

[clients/vscode/src/webview/sync/chat-panel-bridge.ts](clients/vscode/src/webview/sync/chat-panel-bridge.ts): on receiving `{ type: 'init', chatId }` from the host (today this happens before the bridge is even mounted — move the listener into the bridge module's `mount()` so the order is deterministic), call `useSessionUIStore.getState().setCurrentSession(chatId, null)` **before** calling `mountChatPanelBridge(chatId)`. OC's ChatContainer then renders for that chat id; ChatInput's send button enables.

For `{ type: 'init', chatId, isDraft: true, projectExternalId }` (Phase 15d), the bridge ALSO calls `openNewSessionDraft({ selectedProjectId: projectExternalId, draftChatId: chatId })`.

### 15a.4 Purge `useChatStore` references in the sync layer

[clients/vscode/src/webview/sync/session-ui-store.ts](clients/vscode/src/webview/sync/session-ui-store.ts), [session-actions.ts](clients/vscode/src/webview/sync/session-actions.ts), [sync-context.ts](clients/vscode/src/webview/sync/sync-context.ts), [streaming.ts](clients/vscode/src/webview/sync/streaming.ts), [chat-panel-bridge.ts](clients/vscode/src/webview/sync/chat-panel-bridge.ts), [chat-panel-bridge.test.ts](clients/vscode/src/webview/sync/chat-panel-bridge.test.ts), [stores/useConfigStore.ts](clients/vscode/src/webview/stores/useConfigStore.ts):

For each file, replace every `useChatStore.*` read/write with the OC equivalent:
| Legacy | OC replacement |
|---|---|
| `useChatStore.chatId` | `useSessionUIStore.currentSessionId` |
| `useChatStore.messages` | `useSessionMessages(currentSessionId)` from sync-context |
| `useChatStore.currentAgent` | `useSelectionStore.getActiveAgentForSession(...)` / per-chat agent fetched by chat-panel-bridge |
| `useChatStore.composerModel/Effort/Profile/Thinking` | OC's per-chat composer state lives inside `ChatInput`'s own `useState` + `useSelectionStore` for sticky model selection. Most of these reads disappear with the legacy components. |
| `useChatStore.contextData` | `useContextStore` (after Phase 15c.5) |
| `useChatStore.permissions/questions` | `useSessionUIStore.pendingPermissions / pendingQuestions` (OC handles inline in ChatContainer) |

`sendMessage` becomes a pure OC dispatch: read `currentSessionId` from `useSessionUIStore`, build the request body from the OC-supplied `(content, providerID, modelID, agent, attachments, ...)` args + draft target if `newSessionDraft.open`, POST via `LisRpcClient.sendChatMessage`. No legacy fallback. No "fallback when empty" — if `providerID`/`modelID` are empty strings, that's a bug in the caller; let the server reject it (it'll fall back to the chat's bound agent's default model, which is the correct semantic).

### 15a.5 Archived session click + sidebar intercept fix

**Files**: [clients/vscode/src/webview/sidebarOC.tsx](clients/vscode/src/webview/sidebarOC.tsx), [clients/vscode/src/chatPanel.ts](clients/vscode/src/chatPanel.ts).

1. Fix intercept signature: `setCurrentSession: (id: string, directory?: string | null) => { vscode.postMessage({ type: 'open_session', chat_id: id, directory }); origSet(id, directory); }`. Host's existing `open_session` handler ignores `directory` for now; preserving the arg removes downstream short-circuits inside OC.
2. Verify archived chats load in `ChatPanelManager.openOrFocus`. If `GET /api/chats/{id}/messages` 404s for archived, relax `ChatsController.GetMessages` to return history regardless of archived state (touch only after repro confirms it).

**Verify Phase 15a**:
- Open any chat from the sidebar — chat panel renders OC's `ChatContainer` only (no legacy chrome, no left/right sidebars duplicated inside the panel). Send button is enabled.
- Type "ping" and send — message appears in the panel and on the backend (`GET /api/chats/{id}/messages` shows it). Assistant streams back.
- Expand "archived" in the sidebar, click an archived chat — panel opens with history, send still works.
- `bun run typecheck` is clean (no dangling imports of deleted modules).
- `grep -rn useChatStore clients/vscode/src/webview/` returns zero matches.

---

## Phase 15b — Add-Project picker as dedicated editor-area panel

### 15b.1 Host command + panel manager

**Files**:
- [clients/vscode/src/extension.ts](clients/vscode/src/extension.ts) — register `lis.openAddProject` command.
- [clients/vscode/src/addProjectPanel.ts](clients/vscode/src/addProjectPanel.ts) (new) — `AddProjectPanelManager` (analogue of `ChatPanelManager`, singleton: at most one open at a time, focus-existing-or-create).
- [clients/vscode/package.json](clients/vscode/package.json) — declare `lis.openAddProject` under `contributes.commands` (no menu binding, sidebar invokes it via postMessage).

`AddProjectPanelManager.openOrFocus()`:
- Creates a `vscode.WebviewPanel('lis.addProject', 'Add Project', vscode.ViewColumn.Active, { enableScripts: true, localResourceRoots: [dist] })`.
- HTML mirrors `chatPanel.ts.buildHtml` but loads `dist/addProject.js` (new esbuild entry, IIFE, browser) and sets `window.__LIS_ADD_PROJECT_MODE = true`.
- Routes the existing `host_fs` and `lis.log` messages the same as other panels (reuses `installHostFsHandler` from [hostFs.ts](clients/vscode/src/hostFs.ts)).
- Adds handler for `{ type: 'add_project_committed', path, label?, color? }` → calls `POST /api/projects` via `LisHttpClient`, then `panel.dispose()`.

### 15b.2 New webview entry point

**Files**:
- [clients/vscode/src/webview/addProject.ts](clients/vscode/src/webview/addProject.ts) (new) — sibling to `index.ts`, branches off `__LIS_ADD_PROJECT_MODE`. Bootstraps `LisRpcClient.install`, `installProjectsSync`, waits for `lis.init`, mounts `AddProjectApp`.
- [clients/vscode/src/webview/addProjectApp.tsx](clients/vscode/src/webview/addProjectApp.tsx) (new, ~80 LOC) — renders `DirectoryExplorerDialog` with `open={true}` always; `onOpenChange(false)` posts `{ type: 'close_add_project' }` to host. Wraps it in `I18nProvider` + `TooltipProvider` + dark scope, exactly like `sidebarOC.tsx`.
- [clients/vscode/esbuild.mjs](clients/vscode/esbuild.mjs) — add a third config for `src/webview/addProject.ts` → `dist/addProject.js`.
- [clients/vscode/src/webview/components/session/DirectoryExplorerDialog.tsx](clients/vscode/src/webview/components/session/DirectoryExplorerDialog.tsx) — change ONLY the layout-mode predicate at the top to also opt into `MobileOverlayPanel` when `__LIS_ADD_PROJECT_MODE` is true. Body of the component stays byte-identical to OC. Seed initial path from `useProjectsStore.getState().getActiveProject()?.path ?? useProjectsStore.getState().projects[0]?.path ?? '~'`.

### 15b.3 Sidebar wires the toolbar button to the host command

**Files**: [clients/vscode/src/webview/sidebarOC.tsx](clients/vscode/src/webview/sidebarOC.tsx) — `sessionEvents.onDirectoryRequest` (already exists) now triggers `vscode.postMessage({ type: 'open_add_project' })` instead of opening the inline dialog. The local `setIsDirectoryDialogOpen(true)` in [SessionDialogs.tsx](clients/vscode/src/webview/components/session/SessionDialogs.tsx) becomes a no-op for VS Code (or just remove the listener and let host take over).

**Verify**: click `+ Add project` in the sidebar header. An editor tab titled "Add Project" opens (full editor width). Inside, the OC dialog renders as a centered card with the active project's path pre-filled, directory list, "Show hidden" toggle, "clone repository" link. Pick a folder → tab closes, project appears in sidebar. Click `+ Add project` again with the same path → "Added" badge appears.

---

## Phase 15c — Composer chrome: providers / models / effort / thinking / context / pills

### 15c.1 Backend: providers endpoint

**Files**: [Lis.Api/Schemas/Providers/ProviderDto.cs](Lis.Api/Schemas/Providers/ProviderDto.cs) (new), [Lis.Api/Controllers/ProvidersController.cs](Lis.Api/Controllers/ProvidersController.cs) (new), [Lis.Core/IProviderMetadata.cs](Lis.Core/IProviderMetadata.cs) (new), [Lis.Providers/Anthropic/AnthropicProviderMetadata.cs](Lis.Providers/Anthropic/AnthropicProviderMetadata.cs) (new), [Lis.Providers/Anthropic/AnthropicSetup.cs](Lis.Providers/Anthropic/AnthropicSetup.cs) (extend).

- `IProviderMetadata`: `string Id`, `string Name`, `IReadOnlyDictionary<string, ModelMetadata> Models`. `ModelMetadata`: `id`, `name`, `release_date`, `attachment { image, video, audio }`, `reasoning { enabled, supported_efforts }`, `cost { input, output, cache_read, cache_write }`, `limit { context, output }`, `experimental`.
- Anthropic registers a hard-coded `IProviderMetadata` listing the three current models, read-only, ships with the assembly.
- `ProvidersController`: `[HttpGet]` enumerates DI-registered `IProviderMetadata`. Wraps with `default: { model: <LisOptions.DefaultModel>, agent: <LisOptions.DefaultAgent> }`.
- Conditional: `AddAnthropic` only registers when `ANTHROPIC_ENABLED=true`. Empty providers list is valid.
- `[Trace("ProvidersController > List")]`.

### 15c.2 Backend: per-chat `directory` column

**Files**: [Lis.Persistence/Entities/ChatEntity.cs](Lis.Persistence/Entities/ChatEntity.cs), [Lis.Persistence/Migrations/](Lis.Persistence/Migrations/) (`add_chat_directory`), [Lis.Api/Schemas/Chats/ChatDto.cs](Lis.Api/Schemas/Chats/ChatDto.cs) (`ChatDto` + `UpdateChatRequest`).

- Add `[Column("directory")] public string? Directory` on `ChatEntity`. `varchar(1024)`, nullable.
- `ChatDto.From(...)` includes `directory: c.Directory`.
- `UpdateChatRequest` gains `[property: JsonPropertyName("directory")] string? Directory` (treated as "leave unchanged" when null *and not present*; serialize on the wire only when set — match OC's PATCH semantics: explicit `null` clears, absent leaves).
  - Implementation note: ASP.NET binds optional `null` and missing fields the same. To support "clear vs leave", add a `bool DirectoryWasProvided` via custom binder, OR (simpler) require callers to pass `directory: ""` to clear. Pick "" = clear; document in the controller doc-comment.
- `ChatsController.UpdateChat` writes `Directory` when provided, publishes `chat_updated` via `IEventBus`.
- Tools that need cwd (read `IConversationService` consumers — `Lis.Agent` calls into bash/file tools) resolve effective cwd as `chat.Directory ?? chat.Project?.Path`. Locate the call sites by grepping `Lis.Tools/` for cwd/workdir/Path; touch each so the chat-bound directory is honored. Out of scope here if the tools currently take cwd from somewhere else — note the gap and follow up.

### 15c.3 Webview: port `useConfigStore` providers section

**Files**: [clients/vscode/src/webview/stores/useConfigStore.ts](clients/vscode/src/webview/stores/useConfigStore.ts), [clients/vscode/src/webview/lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts), [clients/vscode/src/webview/index.ts](clients/vscode/src/webview/index.ts) (bootstrap call).

Port the providers slice from [OC useConfigStore](file:///C:/bkp/Dev/openchamber/packages/ui/src/stores/useConfigStore.ts) (`providers`, `defaultProviders`, `modelsMetadata`, `loadProviders`, `getProviderById`, the dedup latch). Adapt the loader to call `LisRpcClient.instance().listProviders()` instead of `opencodeClient.withDirectory(...).getProviders()`. Drop OC's per-directory snapshotting — Lis providers are global; load once with no directory key.

Bootstrap: call `useConfigStore.getState().loadProviders()` once `rpc.ready()` resolves in `webview/index.ts` (mirrors how `installProjectsSync` is wired today).

### 15c.4 Webview: port `useModelLists` + `useProviderLogo` + provider SVGs

**Files**:
- [clients/vscode/src/webview/hooks/useModelLists.ts](clients/vscode/src/webview/hooks/useModelLists.ts) — replace 3-line stub with [OC 58-LOC verbatim](file:///C:/bkp/Dev/openchamber/packages/ui/src/hooks/useModelLists.ts).
- [clients/vscode/src/webview/hooks/useProviderLogo.ts](clients/vscode/src/webview/hooks/useProviderLogo.ts) — replace 2-line stub with OC verbatim (98 LOC).
- [clients/vscode/src/webview/components/ui/provider-logos/](clients/vscode/src/webview/components/ui/provider-logos/) — copy SVG assets from OC.
- [clients/vscode/NOTICE.md](clients/vscode/NOTICE.md) — add SVG attribution if MIT-distinct.

Audit `useUIStore` for `favoriteModels`, `recentModels`, `hiddenModels` — port from OC if missing (probably already there since OC sidebar parity stage already pulled in `useUIStore`).

### 15c.5 Webview: port `contextStore`

**Files**: [clients/vscode/src/webview/stores/contextStore.ts](clients/vscode/src/webview/stores/contextStore.ts).

Port verbatim from OC (511 LOC). Pure derived state over the message buffer; no backend work.

### 15c.6 Webview: composer pills wired to per-chat binding

**Files**: [clients/vscode/src/webview/components/chat/ChatInput.tsx](clients/vscode/src/webview/components/chat/ChatInput.tsx) (currently byte-identical to OC), [clients/vscode/src/webview/sync/session-actions.ts](clients/vscode/src/webview/sync/session-actions.ts).

- Drop the `&& !isVSCode` clause at [line 3302](clients/vscode/src/webview/components/chat/ChatInput.tsx). Define `showProjectWorktreePills = true` for all chats (rename for clarity since it's no longer draft-only).
- Pills logic:
  - **Draft chat (`newSessionDraft.open === true && currentSessionId === draftChatId`)**: clicking the project pill mutates `useSessionUIStore.newSessionDraft.selectedProjectId`; clicking the worktree pill mutates `directoryOverride`. Nothing is persisted server-side until first send.
  - **Persisted chat**: clicking project pill calls `LisRpcClient.moveChat(chatId, projectId)` (PATCH `/api/chats/{id}/move` — already exists). Clicking worktree pill calls `LisRpcClient.updateChat(chatId, { directory })` (new method, hitting `PATCH /api/chats/{id}`). Both publish SSE `chat_updated` → sidebar re-buckets in <300ms.
- The `availableWorktreesByProject` map already feeds the worktree dropdown — verified during the directory-mapping work earlier this session.

**Verify Phase 15c**: open any chat (persisted) — composer shows provider+model pill ("Anthropic / claude-sonnet-4-6") with logo, effort dropdown (`low/medium/high/maximum`) when the model declares `reasoning.supported_efforts`, thinking-mode toggle, context-window bar populated. Click the project pill → dropdown with all projects → switch → chat moves in sidebar within ~300ms. Click the worktree pill → dropdown with the project's worktrees → switch → next tool call uses the new cwd.

---

## Phase 15d — Draft session flow

### 15d.1 Backend: lazy chat creation on first message

**Files**: [Lis.Api/Controllers/ChatsController.cs](Lis.Api/Controllers/ChatsController.cs), [Lis.Api/Schemas/Chats/ChatDto.cs](Lis.Api/Schemas/Chats/ChatDto.cs) (`SendMessageRequest` gains `project_id?`, `directory?`).

- `POST /api/chats/{chatId}/messages`: if `FindByExternalIdAsync` returns null AND the request carries `project_id` (lookup as external id, 404 if it doesn't exist), create the row with `external_id=chatId`, `name="New Chat"`, `project_id=lookup(project_id)`, `directory=request.directory`. Publish `chat_created` via `IEventBus`. Continue with the send.
- Idempotent: retry with the same id finds the existing row from the first call.

### 15d.2 Extension host: open draft panel without REST round-trip

**Files**: [clients/vscode/src/extension.ts](clients/vscode/src/extension.ts) (`lis.newChat`), [clients/vscode/src/chatPanel.ts](clients/vscode/src/chatPanel.ts) (`openOrFocus(chatId, title?, { isDraft?, projectExternalId? })`).

- `lis.newChat` no longer calls `createChatViaRest`. It mints `chatId = web:draft-${randomUUID()}`, then `panels.openOrFocus(chatId, 'New Chat', { isDraft: true, projectExternalId })`.
- `openOrFocus` records the draft state on the `ChatPanel` entry and forwards `{ type: 'init', chatId, isDraft, projectExternalId }` to the webview right after `lis.init`.
- Reopening the same draft chatId (re-clicking `+ New session` while draft tab is open) just focuses the existing panel — already the default behaviour of `openOrFocus`.

### 15d.3 Webview: open the draft on init

**Files**: [clients/vscode/src/webview/sync/chat-panel-bridge.ts](clients/vscode/src/webview/sync/chat-panel-bridge.ts) (or wherever the panel's `init` envelope is consumed today).

On `{ type: 'init', chatId, isDraft: true, projectExternalId }`:
1. Resolve default project: `projectExternalId` > sidebar's `useProjectsStore.activeProjectId` > first project > none (in which case the pill shows "Pick a project" prompt and Send is disabled until the user picks one).
2. `useSessionUIStore.getState().openNewSessionDraft({ selectedProjectId: defaultProject?.id, draftChatId: chatId, directoryOverride: null })`. The `draftChatId` field is a small extension to OC's `NewSessionDraftState` (today OC doesn't pre-mint).
3. `useSessionUIStore.getState().setCurrentSession(chatId, null)` — `ChatContainer` now renders with `currentSessionId === draftChatId` + `draftOpen === true`, OC's empty-draft surface (placeholder + composer focus).

### 15d.4 Webview: persist the binding on first send

**Files**: [clients/vscode/src/webview/sync/session-actions.ts](clients/vscode/src/webview/sync/session-actions.ts) (`sendMessage`), [clients/vscode/src/webview/lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts) (`sendChatMessage` envelope).

`sendChatMessage(chatId, body)` grows optional `project_id`, `directory`. When `useSessionUIStore.newSessionDraft.open === true && newSessionDraft.draftChatId === chatId`, the action reads `selectedProjectId` + `directoryOverride` from the draft and includes them in the POST.

On 200 response: `useSessionUIStore.getState().closeNewSessionDraft()`. The chat is now real; the next render skips draft mode (still the same `chatId`, no panel swap).

### 15d.5 Composer pills are the source of truth for draft binding

The pills described in §15c.6 already mutate `newSessionDraft.{selectedProjectId, directoryOverride}` in draft mode. No additional wiring needed beyond §15c.6 + §15d.3.

**Verify Phase 15d**:
- From the sidebar header (no project context), click `+ New session`. A chat panel tab opens. The sidebar list is unchanged (no row, no SSE). The composer shows pills defaulted to the sidebar's `activeProjectId` (or first project) and that project's main worktree.
- Switch the project pill to a different project — the worktree pill repopulates with that project's worktrees.
- Type "hello" and send. Within ~300ms a "New Chat" row appears under the selected project in the sidebar. The panel keeps the same id and doesn't swap.
- Re-fire `+ New session` from a project's `+` button — opens a *separate* draft panel for that project (each draft has its own pre-minted id). Send-without-pick is blocked until a project is selected (no implicit default).
- Hard-reload the webview while a draft is unsent — the draft panel re-opens to the same `chatId`, no server row exists yet, composer pills are reset to defaults. (Acceptable: drafts are ephemeral.)

---

## Files modified / created

**Backend**
- [Lis.Api/Schemas/Providers/ProviderDto.cs](Lis.Api/Schemas/Providers/ProviderDto.cs) (new)
- [Lis.Api/Controllers/ProvidersController.cs](Lis.Api/Controllers/ProvidersController.cs) (new)
- [Lis.Core/IProviderMetadata.cs](Lis.Core/IProviderMetadata.cs) (new)
- [Lis.Providers/Anthropic/AnthropicProviderMetadata.cs](Lis.Providers/Anthropic/AnthropicProviderMetadata.cs) (new)
- [Lis.Providers/Anthropic/AnthropicSetup.cs](Lis.Providers/Anthropic/AnthropicSetup.cs) (extend `AddAnthropic`)
- [Lis.Persistence/Entities/ChatEntity.cs](Lis.Persistence/Entities/ChatEntity.cs) (`Directory` column)
- [Lis.Persistence/Migrations/](Lis.Persistence/Migrations/) (`add_chat_directory`)
- [Lis.Api/Schemas/Chats/ChatDto.cs](Lis.Api/Schemas/Chats/ChatDto.cs) (`ChatDto.directory`, `UpdateChatRequest.directory`, `SendMessageRequest.{project_id,directory}`)
- [Lis.Api/Controllers/ChatsController.cs](Lis.Api/Controllers/ChatsController.cs) (`UpdateChat` writes directory; `SendMessage` lazy-creates on missing-with-project_id; both publish `chat_updated`/`chat_created`)
- Tool cwd plumbing audit under `Lis.Tools/` and `Lis.Agent/` so tools honour `chat.Directory ?? chat.Project?.Path`

**Extension host**
- [clients/vscode/src/extension.ts](clients/vscode/src/extension.ts) — `lis.newChat` mints draft id and opens panel; new `lis.openAddProject` command
- [clients/vscode/src/chatPanel.ts](clients/vscode/src/chatPanel.ts) — `openOrFocus` carries `isDraft` + `projectExternalId` through to `init`
- [clients/vscode/src/addProjectPanel.ts](clients/vscode/src/addProjectPanel.ts) (new) — singleton WebviewPanel manager
- [clients/vscode/src/sidebarWebviewProvider.ts](clients/vscode/src/sidebarWebviewProvider.ts) — handle `open_add_project` postMessage → run command
- [clients/vscode/esbuild.mjs](clients/vscode/esbuild.mjs) — third bundle: `addProject.js`
- [clients/vscode/package.json](clients/vscode/package.json) — declare `lis.openAddProject` command

**Webview — entry points**
- [clients/vscode/src/webview/addProject.ts](clients/vscode/src/webview/addProject.ts) (new)
- [clients/vscode/src/webview/addProjectApp.tsx](clients/vscode/src/webview/addProjectApp.tsx) (new)
- [clients/vscode/src/webview/sidebarOC.tsx](clients/vscode/src/webview/sidebarOC.tsx) — fix `setCurrentSession` signature; route `+ Add project` through host command

**Webview — stores/hooks**
- [clients/vscode/src/webview/stores/useConfigStore.ts](clients/vscode/src/webview/stores/useConfigStore.ts) — port providers slice (~500 of 2266 LOC from OC)
- [clients/vscode/src/webview/stores/contextStore.ts](clients/vscode/src/webview/stores/contextStore.ts) — port verbatim (511 LOC)
- [clients/vscode/src/webview/hooks/useModelLists.ts](clients/vscode/src/webview/hooks/useModelLists.ts) — port verbatim (58 LOC)
- [clients/vscode/src/webview/hooks/useProviderLogo.ts](clients/vscode/src/webview/hooks/useProviderLogo.ts) — port verbatim (98 LOC)
- [clients/vscode/src/webview/components/ui/provider-logos/](clients/vscode/src/webview/components/ui/provider-logos/) — SVGs from OC
- [clients/vscode/NOTICE.md](clients/vscode/NOTICE.md) — SVG attribution
- [clients/vscode/src/webview/lib/lis/rpc-client.ts](clients/vscode/src/webview/lib/lis/rpc-client.ts) — `listProviders()`; extend `updateChat`/`sendChatMessage` with `directory` and `project_id`

**Webview — composer**
- [clients/vscode/src/webview/components/chat/ChatInput.tsx](clients/vscode/src/webview/components/chat/ChatInput.tsx) — drop `&& !isVSCode` gate; pills mutate persisted-chat binding via `LisRpcClient` when not in draft

**Webview — draft plumbing**
- [clients/vscode/src/webview/sync/session-actions.ts](clients/vscode/src/webview/sync/session-actions.ts) — `sendMessage` reads draft target, closes draft on first-send success
- [clients/vscode/src/webview/sync/session-ui-store.ts](clients/vscode/src/webview/sync/session-ui-store.ts) — `NewSessionDraftState` gains `draftChatId?: string`
- [clients/vscode/src/webview/sync/chat-panel-bridge.ts](clients/vscode/src/webview/sync/chat-panel-bridge.ts) — on `{ isDraft: true }` init, open draft + set current session

**Webview — picker**
- [clients/vscode/src/webview/components/session/DirectoryExplorerDialog.tsx](clients/vscode/src/webview/components/session/DirectoryExplorerDialog.tsx) — extend layout-mode predicate for `__LIS_ADD_PROJECT_MODE` so it mounts via OC's `MobileOverlayPanel` (the centered-card surface) instead of VS Code's `<Dialog>`; seed initial path from active/first project. Body stays OC-identical.

**Webview — legacy chat surface deletion (Phase 15a)**
Files deleted entirely:
- [clients/vscode/src/webview/store.ts](clients/vscode/src/webview/store.ts) (`useChatStore`)
- [clients/vscode/src/webview/components/composer/](clients/vscode/src/webview/components/composer/) (`Composer.tsx`, `AgentPicker.tsx`, `EffortPicker.tsx`, `ModelInput.tsx`, `ProfilePicker.tsx`, `ThinkingModePicker.tsx`)
- [clients/vscode/src/webview/components/messages/MessageHeader.tsx](clients/vscode/src/webview/components/messages/MessageHeader.tsx) (and rest of dir if unreferenced)
- [clients/vscode/src/webview/components/MessageList.tsx](clients/vscode/src/webview/components/MessageList.tsx)
- [clients/vscode/src/webview/components/StatusBar.tsx](clients/vscode/src/webview/components/StatusBar.tsx)
- [clients/vscode/src/webview/components/ContextPanel.tsx](clients/vscode/src/webview/components/ContextPanel.tsx)
- [clients/vscode/src/webview/components/layout/MainLayout.tsx](clients/vscode/src/webview/components/layout/MainLayout.tsx) (and the rest of `layout/` if every sibling — `Header`, `Sidebar`, `RightSidebar`, etc. — is only consumed by MainLayout)
- [clients/vscode/src/webview/hooks/useSessionActivity.ts](clients/vscode/src/webview/hooks/useSessionActivity.ts) (legacy stub; port OC original only if a remaining consumer needs it)

Files rewritten:
- [clients/vscode/src/webview/app.tsx](clients/vscode/src/webview/app.tsx) — minimal shell, renders `ChatContainer` directly
- [clients/vscode/src/webview/sync/session-ui-store.ts](clients/vscode/src/webview/sync/session-ui-store.ts) — `sendMessage` uses OC args + OC stores only (no legacy fallback)
- [clients/vscode/src/webview/sync/chat-panel-bridge.ts](clients/vscode/src/webview/sync/chat-panel-bridge.ts) — owns `init` listener, sets `useSessionUIStore.currentSessionId`
- [clients/vscode/src/webview/sync/session-actions.ts](clients/vscode/src/webview/sync/session-actions.ts), [sync-context.ts](clients/vscode/src/webview/sync/sync-context.ts), [streaming.ts](clients/vscode/src/webview/sync/streaming.ts), [stores/useConfigStore.ts](clients/vscode/src/webview/stores/useConfigStore.ts), [chat-panel-bridge.test.ts](clients/vscode/src/webview/sync/chat-panel-bridge.test.ts) — purge `useChatStore` references per the mapping table in §15a.4

---

## Verification

End-to-end checklist (Extension Development Host or `code serve-web` against the live backend with a real `lis.authToken`):

1. **Send works** — open any chat from the sidebar, type "ping", click send. Request appears in backend (`GET /api/chats/{id}/messages` shows it within seconds), assistant streams back via SSE in the panel.
2. **Providers API** — `curl -H "Authorization: Bearer $TOKEN" $BASE/api/providers | jq` returns `{ providers: [{ id: "anthropic", models: { "claude-sonnet-4-6": {...}, "claude-opus-4-7": {...}, "claude-haiku-4-5-20251001": {...} } }], default: { model: "...", agent: "..." } }`.
3. **Archived click** — sidebar → archived → click a chat → panel opens with history, send still works.
4. **Add-Project modal** — click `+ Add project` in sidebar header → editor tab opens displaying a backdrop overlay + centered card with the OC picker. Default path is the active project. Pick a folder → tab disposes, project appears in sidebar. Pressing Escape or clicking backdrop also dismisses.
5. **Composer chrome** — open any persisted chat. Provider+model pill with Anthropic logo, effort dropdown, thinking-mode toggle, context-window bar populated. Click provider+model pill → OC picker with capability badges + reasoning effort chips.
6. **Per-chat project pill** — click project pill → dropdown → switch → sidebar re-buckets within ~300ms (SSE `chat_updated`).
7. **Per-chat worktree pill** — under a project with multiple worktrees, click worktree pill → dropdown lists them → switch → backend tool calls in subsequent turns cwd into the new path (verified by sending a `pwd` / `ls` prompt).
8. **Draft flow** — `+ New session` from sidebar header → new editor tab "New Chat" with no DB row. Composer pills default to active sidebar project. Send "hello" → row appears in sidebar within ~300ms, same panel, no swap.
9. **Parallel drafts** — open `+ New session` for project A (tab 1), then for project B (tab 2). Both panels live independently, each with its own composer pills.
10. **Regression** — projects CRUD, the "Other" group for unbound chats, the dedup of `(main)` worktree row, archived sessions, all still work.

---

## Out of scope (explicit non-goals)

- **Autocomplete data sources** — `useCommandsStore`, `useSkillsStore`, `useSnippetsStore`, `useFileSearchStore` stay stubbed. The `@`/`#`/`/` pickers will continue to show empty results until a follow-up stage. The textarea itself works; only the dropdown contents are missing.
- **Voice, terminal, MCP, PWA stubs** — `useBrowserVoice`, `useTerminalStore`, `useMcpStore`, `useMcpConfigStore`, `useFilesViewTabsStore`, `usePwaDetection`, etc. stay stubbed. Most are not meaningful inside a VS Code webview.
- **Quota / billing UI** — `useQuotaStore` stays stubbed; no Lis backend for quotas yet.
- **Magic prompts / skills catalog** — same deferral as the original sidebar-parity plan.
- **True OS-level modal for Add-Project** — not possible inside a VS Code extension. The editor-tab approach is the closest analogue.

These will be a follow-up stage if needed.
