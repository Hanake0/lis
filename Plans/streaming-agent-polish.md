# Agent Polish, Message Metadata & Streaming

## Context

The web channel and extension were built fast. Before adding streaming, tool call visibility, and agent controls, we need to fix structural issues: monolithic types, god-files, mixed responsibilities. This plan has two phases — refactor first, then features.

---

## Phase A: Structural Refactoring

### Problem inventory

**Backend — `Lis.Channels/Web/Schemas/`**
- `ServerFrame.cs` is a god class: one flat type with nullable fields for every frame type, plus factory methods, plus `ChatSummary` and `MessageRow` DTOs all in one file. Violates SRP and OCP — every new frame type adds more nullable fields.
- `ClientFrame.cs` is the same pattern: one flat class for all inbound frame types.

**Backend — `Lis.Channels/Web/`**
- `WebChannelController.cs` handles WS lifecycle, frame dispatch, message handling, history queries, and chat listing — too many responsibilities for one controller.

**Frontend — `clients/vscode/src/`**
- `lisClient.ts` mixes frame type definitions with the WebSocket client class.
- `chatPanel.ts` mixes the `ChatPanel` interface, `ChatPanelManager` class, the bundled HTML template string, and `generateNonce` helper in one file.
- `sessionsTreeProvider.ts` mixes `SessionItem`, `SessionsTreeProvider`, and `relativeTime` helper — minor but should separate types.

**Frontend — `clients/vscode/media/`**
- `chat.js` is a 177-line IIFE handling UI wiring, host messaging, frame dispatch, rendering, typing indicators, and scrolling — all in one function. No separation between concerns.

### Refactoring plan

#### Backend schemas → discriminated union with separate DTOs

Split `Lis.Channels/Web/Schemas/` into:
- `ServerFrame.cs` — keep the class but make it a clean discriminated union base. Fields stay flat (JSON serialization requires it for the WS protocol), but factory methods grouped logically. Remove DTOs from this file.
- `ChatSummary.cs` — own file
- `MessageRow.cs` — own file (will grow with token data + tool call info)
- `ClientFrame.cs` — stays flat (it's small), but add new fields for overrides

#### Backend controller → split frame handlers

`WebChannelController.cs` currently has `DispatchClientFrameAsync` → `HandleMessageFrameAsync`, `HandleHistoryFrameAsync`, `HandleListChatsFrameAsync`. These are fine as private methods for now — the controller is the WS endpoint and dispatch is its job. But extract the history/list query logic into a dedicated `WebChatQueryService` if it gets much bigger. For now, the controller stays but new frame handlers (agent management) go in cleanly.

#### Frontend types → separate files

- `clients/vscode/src/types.ts` — all frame type definitions (`ServerFrame`, `ClientFrame`, `ChatSummary`, `MessageRow`, `ConnectionState`, `DispatchMode`, `AgentSummary`)
- `clients/vscode/src/lisClient.ts` — imports types from `types.ts`, only contains the `LisClient` class
- `clients/vscode/src/chatPanel.ts` — only `ChatPanelManager`. Move `ChatPanel` interface inline (it's private). Extract HTML template to `clients/vscode/media/chat.html` or keep as const but move `generateNonce` to a `util.ts`.
- `clients/vscode/src/sessionsTreeProvider.ts` — keep `SessionItem` here (it's tightly coupled to the provider), but `relativeTime` moves to `util.ts`.

#### Frontend webview → TypeScript bundle via esbuild

Delete `media/chat.js`. Add a second esbuild entry point for the webview bundle: `src/webview/index.ts` → `dist/chat.js` (IIFE format, browser platform, no externals). This gives us TypeScript type-checking on webview code and proper module splitting:

- `src/webview/index.ts` — entry: acquires vscode API, wires host message listener, delegates to dispatcher
- `src/webview/types.ts` — webview-specific types (or import shared types from `src/types.ts`)
- `src/webview/dispatcher.ts` — frame dispatch switch, routes to handlers
- `src/webview/streaming.ts` — chunk accumulation, thinking indicator, progressive text rendering
- `src/webview/toolBlocks.ts` — tool call/result collapsible blocks
- `src/webview/metadata.ts` — message metadata storage and expandable panel
- `src/webview/composer.ts` — controls (agent, model, effort, tool profile, save)
- `src/webview/renderer.ts` — message rendering, scrolling, typing bubble, DOM helpers

Update `esbuild.mjs` to build both entry points. Update `chatPanel.ts` to reference `dist/chat.js` instead of `media/chat.js` and add `dist/` to `localResourceRoots`.

---

## Phase B: Feature Implementation

### B1: Streaming Protocol

#### New ServerFrame types

- `"chunk"` — text delta: `{ chat_id, id, delta, done }` — streamed during LLM generation
- `"thinking_chunk"` — thinking delta: `{ chat_id, id, delta, done }` — streamed during thinking phase, separate from text
- `"tool_start"` — tool invocation begins: `{ chat_id, id, tool_name, tool_args }` — own message, not appended to assistant
- `"tool_result"` — tool completed: `{ chat_id, id, tool_name, result, error? }` — own message

#### Design decisions (per user feedback)

1. **Tool calls are standalone messages** — `tool_start` and `tool_result` are their own frames/entities, not appended to assistant messages. They already are separate `MessageEntity` rows with `Role == "tool"`.

2. **Usage metadata accompanies the message** — token counts are fields on the `message` frame itself, NOT a separate `message_meta` frame. The existing `MessageRow` gets `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_creation_tokens`, `thinking_tokens` fields. The final `message` frame carries them.

3. **Channel-agnostic streaming** — add streaming methods to `IChannelClient` interface itself (not a separate `IStreamSink`). WhatsApp's implementation is no-op. This keeps the architecture unified — one interface for all channel capabilities.
   ```csharp
   // Added to IChannelClient
   Task SendChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct);
   Task SendThinkingChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct);
   Task SendToolStartAsync(string chatId, string messageId, string toolName, string args, CancellationToken ct);
   Task SendToolResultAsync(string chatId, string messageId, string toolName, string result, string? error, CancellationToken ct);
   ```
   Default interface methods return `Task.CompletedTask` — channels that don't support streaming do nothing.

4. **Overrides on the message frame** — the client sends `model`, `effort`, `tool_profile` on the `message` client frame itself. No server-side per-connection state. Stateless.
   ```
   ClientFrame "message": { chat_id, text, dispatch, model?, effort?, tool_profile? }
   ```
   These flow through `IncomingMessage` → `ConversationService.RespondAsync` where they override the agent's defaults.

5. **Full message after streaming** — after all chunks are sent, still send the complete `message` frame with final text + token usage. Same for tool calls: `tool_start` + `tool_result` during execution, then the full `message` frame (role=tool) in history. Client uses the final frame as source of truth, chunks are progressive rendering only.

6. **Agent switch is its own frame** — `set_agent` changes the chat's agent. `get_agent` returns current config + agent list. `save_overrides` persists the current message overrides to the agent entity.

### B2: IChannelClient extension

Add to `Lis.Core/Channel/IChannelClient.cs`:
```csharp
Task SendChunkAsync(string chatId, string messageId, string delta, bool done, CancellationToken ct = default)
    => Task.CompletedTask;
Task SendToolStartAsync(string chatId, string messageId, string toolName, string args, CancellationToken ct = default)
    => Task.CompletedTask;
Task SendToolResultAsync(string chatId, string messageId, string toolName, string result, string? error, CancellationToken ct = default)
    => Task.CompletedTask;
```

Default interface methods — `WhatsAppClient` doesn't need any changes. `WebChannelClient` overrides them to publish the corresponding `ServerFrame`.

### B3: ToolRunner streaming integration

**Current gaps — thinking is broken in multiple ways:**

1. **Thinking config uses raw `ExtensionData` dicts** — `ConversationService` stuffs `{"type": "enabled", "budget_tokens": N}` into `PromptExecutionSettings.ExtensionData["thinking"]`. This bypasses the SDK's proper `ChatOptions.WithThinking()` / `WithAdaptiveThinking()` path. It may work (SK passes extension data through), but it's fragile and doesn't use the SDK's typed API.

2. **Missing `"display": "summarized"`** — Starting with Claude Opus 4.7, thinking content is omitted by default. Thinking blocks still appear in the stream but their `thinking` field is empty. We must set `"display": "summarized"` in the thinking parameters to get actual thinking text streamed. Without this, the user sees a long pause then output — no thinking visibility.

3. **Thinking content not captured in ToolRunner** — `StreamResponseAsync` only reads `chunk.Content` (text). Thinking arrives as `TextReasoningContent` items in the streaming chunks. The current code silently drops them.

4. **Thinking tokens always 0** — `AnthropicUsageExtractor` returns `ThinkingTokens: 0` because it doesn't look for the right key in `UsageDetails.AdditionalCounts`. The Anthropic API does return thinking tokens in usage — we need to find and read the correct key, or extract from the raw `Usage` object if the SDK doesn't map it.

**Fix — three layers:**

**Layer 1: Fix thinking config in ConversationService**
- Add `"display": "summarized"` to the `ExtensionData["thinking"]` dict so thinking text is actually streamed
- Add a `ThinkingDisplayHandler` (delegating handler, same pattern as `CacheControlHandler`) as a safety net to ensure the field reaches the API
- Keep `IChatCompletionService` + `PromptExecutionSettings` (full `IChatClient` migration is too large for this task — too many SK type dependencies)

**Layer 2: Capture and stream thinking in ToolRunner**
- On each streaming chunk, check for `TextReasoningContent` items (the `IChatClient` path) or `ThinkingContent` inner content
- Call `channelClient.SendThinkingChunkAsync(...)` for thinking deltas
- Accumulate thinking text for persistence

**Layer 3: Fix thinking token counting**
- Investigate what key the Anthropic API uses for thinking tokens in the usage response
- Update `AnthropicUsageExtractor` to read the correct field
- If SDK 5.10 doesn't expose it, consider upgrading or extracting from raw metadata

**ToolRunner.StreamResponseAsync changes:**
- Accept `IChannelClient` + `chatId` + pre-generated `messageId`
- On each chunk, inspect content type:
  - If thinking content (`TextReasoningContent`) → call `channelClient.SendThinkingChunkAsync(...)` and accumulate
  - If text content → call `channelClient.SendChunkAsync(chatId, messageId, chunk.Content, false, ct)`
- After loop: call `channelClient.SendChunkAsync(chatId, messageId, "", true, ct)` (done signal)
- Before each tool invocation: call `channelClient.SendToolStartAsync(...)`
- After each tool invocation: call `channelClient.SendToolResultAsync(...)`

`ConversationService.RespondAsync` changes:
- Generate `messageId` before the tool runner loop: `$"web:{Guid.NewGuid():N}"` (or channel-agnostic format)
- Pass `channelClient`, `chatId`, `messageId` to `ToolRunner.RunAsync`
- After streaming, still call `channelClient.SendMessageAsync` with the complete text (source of truth)
- The `message` frame now includes token usage fields

### B4: IncomingMessage overrides

Add to `IncomingMessage`:
```csharp
public string? ModelOverride { get; init; }
public string? EffortOverride { get; init; }
public string? ToolProfileOverride { get; init; }
```

`WebChannelController.HandleMessageFrameAsync` reads `frame.Model`, `frame.Effort`, `frame.ToolProfile` and passes them through.

`ConversationService.RespondAsync` applies:
- `message.ModelOverride ?? agent.Model` for model selection
- `message.EffortOverride ?? agent.ThinkingEffort` for effort
- `message.ToolProfileOverride ?? agent.ToolProfile` for tool filtering

### B5: Agent management frames

New client frames:
- `set_agent { chat_id, agent_name }` — switch chat's agent (reuse `AgentCommand` logic)
- `get_agent { chat_id }` — returns `agent_info` frame for this chat
- `list_agents` — returns `agent_list` frame with all available agents
- `save_overrides { chat_id, model?, effort?, tool_profile? }` — persist to agent entity

New server frames:
- `agent_info { chat_id, agent_name, display_name, model, effort, tool_profile }` — current agent for this chat
- `agent_list { agents[] }` — all available agents (response to `list_agents`)

New DTO in `Lis.Channels/Web/Schemas/AgentSummary.cs`:
```csharp
public sealed class AgentSummary {
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required string Model { get; init; }
    public string? Effort { get; init; }
    public string? ToolProfile { get; init; }
    public bool IsDefault { get; init; }
}
```

### B6: History enrichment

`MessageRow` gets token usage fields. The history query in `WebChannelController` projects them from `MessageEntity`. Tool-role messages are included in history with their `Role` field — the client groups them visually.

### B7: Extension — streaming renderer

In `chat.js` (will be `src/webview/streaming.ts` after refactor):
- `thinking_chunk` frame: find or create a thinking indicator element by `id`. Show "Thinking..." with expanding dots or a collapsible thinking block. Append thinking `delta` to a hidden/collapsible area. On first thinking chunk, hide typing bubble.
- `chunk` frame: find or create the message element by `id`, append `delta` to textContent. On first text chunk, transition from thinking indicator to message content. On `done`, finalize.
- After all chunks, the full `message` frame arrives — replace the streamed content with the authoritative text (handles any mismatch).
- Streaming cursor animation on the message being built.
- Thinking block is collapsible — collapsed by default after response completes, expandable on click.

### B8: Extension — tool call blocks

- `tool_start`: insert a collapsible block (tool name + args) after the current streaming message. Compact, monospace.
- `tool_result`: update the block with result. Green check or red X.
- The subsequent full `message` (role=tool) replaces/confirms the content.
- Blocks are collapsible (click to expand/collapse args and result).

### B9: Extension — message metadata

- Each assistant `message` frame now carries token usage.
- Store on the DOM element (data-* attributes or a WeakMap).
- Small badge on message bubble (e.g., "1.2k tokens").
- Click to expand inline panel: input/output/cache_read/cache_creation/thinking tokens, model name, timestamp.

### B10: Extension — composer controls

Add a controls row above the textarea in the composer:
- Agent dropdown (populated from `agent_info.agents[]`)
- Model text input (editable, prefilled from current agent)
- Effort selector: low / medium / high
- Tool profile selector: minimal / standard / coding / full
- "Save" button — sends `save_overrides` to persist

Overrides are sent on each `message` frame — no separate `set_overrides` needed since overrides travel with the message.

Agent change sends `set_agent` frame, which triggers a new `agent_info` response to update current config. Agent list is fetched separately via `list_agents`.

On panel init, send `get_agent` (for current chat config) and `list_agents` (for dropdown population).

---

## Implementation Order

### Step 1: Refactor backend schemas (commit)
- Extract `ChatSummary` → own file
- Extract `MessageRow` → own file
- Clean up `ServerFrame.cs` factory methods

### Step 2: Refactor frontend types + structure (commit)
- Create `src/types.ts` with all frame/DTO types
- Create `src/util.ts` with `generateNonce`, `relativeTime`
- Update imports in `lisClient.ts`, `chatPanel.ts`, `sessionsTreeProvider.ts`
- Restructure `chat.js` with clear sections

### Step 3: Fix thinking config + token counting (commit)

The full `IChatClient` migration is too large — `ToolRunner`, `ContextWindowBuilder`, `ChatHistorySerializer`, and `CompactionService` are deeply entangled with SK types (`ChatHistory`, `AuthorRole`, `FunctionCallContent`, `Kernel`). Keep `IChatCompletionService` for now, but fix thinking properly:

- **Fix thinking config in `ConversationService`**: Replace the raw `ExtensionData["thinking"]` dict with a properly structured dict that includes `"display": "summarized"`. The SK adapter passes `ExtensionData` through to the underlying `IChatClient`'s `ChatOptions`, which the Anthropic SDK maps to the API request. The dict must include the `display` field for thinking content to be streamed.
- **Add `ThinkingDisplayHandler`** (delegating handler, same pattern as `CacheControlHandler`): intercepts the HTTP request and ensures `thinking.display` is `"summarized"` in the request JSON. This is the safety net in case `ExtensionData` doesn't pass the `display` field through correctly. Already proven pattern in the codebase.
- **Fix `AnthropicUsageExtractor`**: The Anthropic API returns thinking tokens — investigate the correct key in the SDK's `UsageDetails.AdditionalCounts` or raw metadata. If SDK 5.10 doesn't map it, extract from the inner `Usage` object on the metadata.
- **Verify**: Send a message with thinking enabled, confirm thinking blocks stream with content (not empty), confirm thinking tokens are counted.

### Step 4: Add streaming to IChannelClient + WebChannelClient (commit)
- Add default methods to `IChannelClient` (including `SendThinkingChunkAsync`)
- Implement in `WebChannelClient`
- Add new frame factory methods to `ServerFrame`

### Step 5: Wire streaming in ToolRunner + ConversationService (commit)
- ToolRunner accepts channel + IDs, calls streaming methods during iteration
- Detect and stream thinking content (`TextReasoningContent` items)
- ConversationService generates messageId upfront, passes to ToolRunner
- Full message still sent at end with token usage

### Step 6: Add IncomingMessage overrides + agent frames (commit)
- Add override fields to `IncomingMessage`
- Add override fields to `ClientFrame`
- Handle `set_agent`, `get_agent`, `list_agents`, `save_overrides` in controller
- Add `AgentSummary` DTO, `agent_info` + `agent_list` frames
- Apply overrides in `ConversationService.RespondAsync`

### Step 7: Enrich MessageRow + history query (commit)
- Add token fields to `MessageRow`
- Include tool messages in history
- Update LINQ projection

### Step 8: Extension streaming + thinking + tool blocks (commit)
- Add new frame types to `types.ts`
- Handle `chunk`, `thinking_chunk`, `tool_start`, `tool_result` in webview
- Thinking block (collapsible, collapsed by default after completion)
- CSS for streaming cursor, thinking block, tool blocks

### Step 9: Extension metadata + composer controls (commit)
- Message metadata expand UI
- Composer controls row (agent, model, effort, tool profile)
- Wire `get_agent` on init, `list_agents` for dropdown, `set_agent` on change, `save_overrides` on save
- CSS for controls and metadata panel

---

## Files to modify

**Backend:**
- `Lis.Core/Channel/IChannelClient.cs` — add streaming default methods
- `Lis.Core/Channel/IncomingMessage.cs` — add override fields
- `Lis.Channels/Web/Schemas/ServerFrame.cs` — clean up, add new frame types + token fields
- `Lis.Channels/Web/Schemas/ChatSummary.cs` — new file (extracted)
- `Lis.Channels/Web/Schemas/MessageRow.cs` — new file (extracted, enriched)
- `Lis.Channels/Web/Schemas/AgentSummary.cs` — new file
- `Lis.Channels/Web/Schemas/ClientFrame.cs` — add override + agent fields
- `Lis.Channels/Web/WebChannelClient.cs` — implement streaming methods
- `Lis.Channels/Web/WebChannelController.cs` — handle agent frames, enrich history
- `Lis.Agent/ToolRunner.cs` — accept channel client, call streaming methods
- `Lis.Agent/ConversationService.cs` — generate ID upfront, apply overrides, pass channel to ToolRunner

**Frontend:**
- `clients/vscode/src/types.ts` — new file (all types)
- `clients/vscode/src/util.ts` — new file (helpers)
- `clients/vscode/src/lisClient.ts` — remove types, import from types.ts
- `clients/vscode/src/chatPanel.ts` — extract helpers, import types
- `clients/vscode/src/sessionsTreeProvider.ts` — import from types.ts + util.ts
- `clients/vscode/media/chat.js` — restructure, add streaming/tools/metadata/controls
- `clients/vscode/media/chat.css` — new component styles

---

## Verification

1. `dotnet build` — backend compiles clean after schema refactor
2. `npm run build` — extension compiles clean after type refactor
3. Start API + F5 extension — existing chat still works (refactor didn't break anything)
4. Send a message — text appears token-by-token (streaming)
5. Ask something that triggers a tool — tool block appears with name/args → result
6. Click assistant message — metadata panel expands with token counts
7. Change model in composer — next response uses new model (check API logs)
8. Switch agent via dropdown — verify new agent config reflected
9. "Save" button — verify agent entity updated in DB
10. Close and reopen chat — history shows token data on messages
