# Group Interaction: Comprehensive Improvement Plan

## Context

Lis handles groups at a basic level: `IsGroup` flag, `RequireMention` toggle, per-chat
`AllowedSenders` list, and sender-name prefixes in context. Several critical gaps exist
in authorization, context building, token usage, webhook mapping, and usability. This plan
addresses all of them with micro-commits for each logical task.

---

## Current Behavior

| Aspect | Behavior |
|--------|----------|
| Group detection | `@g.us` suffix → `ChatEntity.IsGroup = true` |
| Authorization | Owner → AllowedSenders → RequireMention blocks rest → deny |
| Non-allowed msgs | Persisted + included in full context (token waste) |
| Mention detection | **Not implemented** — RequireMention = deny-all |
| System prompt | No group awareness (same prompt for 1-on-1 and groups) |
| Reply/quote | `QuotedMessage` in webhook BUT wrong field name + never mapped |
| Debounce | Same 3s for all chats |
| Webhook mapping | Several GOWA fields missing or misnamed |

---

## Task 0: Fix webhook payload mapping (map ALL GOWA fields)

**Problem:** The current `WebhookPayload` has incomplete/incorrect field mappings.

**GOWA webhook structure** (from `go-whatsapp-web-multidevice/src/infrastructure/whatsapp/event_message.go`):
```json
{
  "event": "message",
  "device_id": "628123456789@s.whatsapp.net",
  "payload": {
    "id", "timestamp", "is_from_me",
    "chat_id", "chat_lid",
    "from", "from_lid", "from_name",
    "body",
    "replied_to_id", "quoted_body",
    "forwarded", "view_once",
    "image": { "path": "...", "caption": "..." }, ...
  }
}
```

**Changes:**

`WebhookPayload.cs`:
- Fix `quoted_message` → `quoted_body` (rename `QuotedMessage` to `QuotedBody`)
- Add `RepliedToId` (`replied_to_id`) — ID of the message being replied to
- Add `Forwarded` (`forwarded`) bool
- Add `ViewOnce` (`view_once`) bool
- Add `FromLid` (`from_lid`) string?
- Add `ChatLid` (`chat_lid`) string?

`GowaWebhookController.cs`:
- Map `payload.RepliedToId` → `IncomingMessage.RepliedId`
- Map `payload.QuotedBody` → `IncomingMessage.RepliedContent`
- Extract `envelope.DeviceId` as bot JID (no startup fetch needed — it's on every webhook)

`IncomingMessage.cs`:
- Add `RepliedContent` (string?) — text of the quoted message

`MessageEntity.cs`:
- Add `reply_content` (text?) — persisted quoted text

`ConversationService.cs`:
- Persist `RepliedContent` in `PersistMessageAsync`

**DB:** Greenfield — rewrite initial migration to include `reply_content`.

**Commit:** `🔧 fix(channel): complete webhook payload mapping with all GOWA fields`

---

## Task 1: Smart context windowing for non-allowed messages

**Problem:** All group messages are loaded into context, including noise from senders
whose messages the bot never responded to. Token waste + context pollution.

**Design:** In `ContextWindowBuilder`, apply a sliding window. "Relevant" messages are
those the bot responded to or should respond to (based on the same `ShouldRespond` logic):
- Bot's own responses (assistant/tool messages)
- Messages from senders that triggered a response
- When `RequireMention` is enabled, only mention-triggered messages count as relevant

For consecutive non-relevant messages before a relevant one, keep only the last N.
N is configurable **per-chat** via `ChatEntity.GroupContextMessages` (nullable int,
null = use `LisOptions.GroupContextMessages` default of 5).

**Algorithm:**
1. First pass: identify "relevant" message indices (messages where `IsFromMe` or
   where the next bot response exists, or where `ShouldRespond` would return true)
2. Second pass: for runs of non-relevant messages, keep only last N before the next
   relevant message
3. Only apply in group chats (`chat.IsGroup`)

**Files:**
- `Lis.Core/Configuration/LisOptions.cs` — add `GroupContextMessages` (int, default 5)
- `Lis.Persistence/Entities/ChatEntity.cs` — add `GroupContextMessages` (int?, null = global)
- `Lis.Agent/ContextWindowBuilder.cs` — implement windowing in `Build()`
- `Lis.Agent/ConversationService.cs` — pass group context config to builder
- `Lis.Tools/ConfigPlugin.cs` — add `group_context_messages` to get/set
- `Lis.Tests/Agent/ContextWindowBuilderTests.cs` — test windowing

**Commit:** `✨ feat(agent): smart context windowing for non-relevant group messages`

---

## Task 2: Implement mention detection + OpenGroup flag

**Problem:** `RequireMention = true` is a kill switch. No mention detection exists.
Also, groups without AllowedSenders deny all non-owners.

**GOWA doesn't send `mentioned_jids`** — no upstream changes planned.

**Mention detection strategy:**
1. **Reply-to-bot** — if `RepliedToId` resolves to a bot message (`IsFromMe`), treat as
   implicit mention. Lookup in DB: `WHERE external_id = repliedToId AND is_from_me = true`.
2. **Text pattern** — check if body contains bot's display name (from agent config or
   GOWA push name). Case-insensitive substring match.
3. **Bot JID** — extracted from `envelope.DeviceId` on each webhook (no startup fetch needed).

**New: OpenGroup flag** — `ChatEntity.OpenGroup` (bool, default false). When true, all
group members can trigger the bot without being in AllowedSenders. `OpenGroup` does NOT
bypass `RequireMention` — a group can be open (anyone can talk) but still require a mention.

**Updated ShouldRespond logic:**
```
1. !chat.Enabled → deny
2. sender == ownerJid → allow
3. sender in AllowedSenders → allow
4. isGroup && chat.OpenGroup → allow (skips allowlist, NOT RequireMention)
5. isGroup && chat.RequireMention && message.IsBotMentioned → allow
6. deny
```

Wait — steps 4 and 5 need refinement. If OpenGroup is true AND RequireMention is true:
- Any sender is "allowed" (open), but they must still mention the bot.
- So: OpenGroup removes the AllowedSenders gate. RequireMention adds a mention gate.
- These are independent axes.

**Revised logic:**
```
1. !chat.Enabled → deny
2. sender == ownerJid → allow
3. Sender authorized? = sender in AllowedSenders OR (isGroup && chat.OpenGroup)
4. If not authorized → deny
5. If isGroup && chat.RequireMention && !message.IsBotMentioned → deny
6. allow
```

**Files:**
- `Lis.Persistence/Entities/ChatEntity.cs` — add `OpenGroup` (bool, default false)
- `Lis.Core/Channel/IncomingMessage.cs` — add `IsBotMentioned` (bool)
- `Lis.Channels/WhatsApp/GowaWebhookController.cs` — detect mention (reply-to-bot check
  needs DB lookup or tracking of own message IDs; text pattern match using bot name)
- `Lis.Agent/AgentService.cs` — rewrite `ShouldRespond` with new logic
- `Lis.Tools/ConfigPlugin.cs` — add `open_group` to get/set
- `Lis.Tests/Agent/AgentServiceTests.cs` — tests for all new paths

**Commit:** `✨ feat(agent): implement mention detection and OpenGroup flag`

---

## Task 3: Include quoted message in context (OpenClaw-inspired)

**Problem:** After Task 0 stores reply data, `ContextWindowBuilder` still ignores it.

**Design:** When a message has `ReplyToId` + `ReplyContent`, include the full quoted text
inline. Truncate to 500 chars. Handle media quotes gracefully.

**Format examples:**
```
[42] Alice (replying to Bob: "The meeting is at 3pm"): I agree
[43] Alice (replying to Bob: [image: sunset photo]): nice!
[44] Alice (replying to Bob: [image]): wow
[45] Alice (replying to [unknown]: "some text"): agreed
```

- If replied-to message is in loaded messages, resolve sender name from there
- If not, show `[unknown]` or omit sender
- For media: use `[{mediaType}]` or `[{mediaType}: {caption}]` if caption exists
- For audio: use `[audio: {transcription}]` if transcription was stored in `Body`
  (audio messages get `<Audio transcript: ...>` set in `ProcessMediaAsync`)
- If `ReplyContent` is null/empty but `ReplyToId` exists, show `(replying to [message])`

**Tool for fetching messages outside context:** Add a `get_message` tool to the tools
plugin that fetches a specific message by ID from DB (with media description). This lets
the AI request full content of truncated/media messages when needed.

**Files:**
- `Lis.Agent/ContextWindowBuilder.cs` — update message building for reply context
- `Lis.Tools/ChatPlugin.cs` (or similar) — add `get_message(id)` tool
- `Lis.Tests/Agent/ContextWindowBuilderTests.cs` — test reply rendering

**Commit:** `✨ feat(agent): include quoted message context in chat history`

---

## Task 4: Group-aware system prompt via `{{group_context}}`

**Problem:** The AI has no idea it's in a group.

**Design:** `{{group_context}}` interpolation variable in `PromptComposer`:
- When `isGroup` → expands to configurable text
- When 1-on-1 → expands to empty string

**Customization:** Add `GroupContextPrompt` field to `AgentEntity` (text, nullable).
- If set: `{{group_context}}` expands to that text
- If null: expands to a hardcoded default

Default:
```
You are in a group chat with multiple participants. Their names appear as prefixes on
messages. Be concise and natural. Address people by name when relevant. Not every
message requires a response — use NO_RESPONSE when a message isn't directed at you
or doesn't need your input. When quoting is appropriate, use [QUOTE] to reply to the
specific message.
```

User changes the text via `set_agent_config group_context_prompt "custom text"` or
similar tool.

**Signature change:** `PromptComposer.BuildAsync` needs `bool isGroup` + `AgentEntity agent`.

**Files:**
- `Lis.Persistence/Entities/AgentEntity.cs` — add `GroupContextPrompt` (text?)
- `Lis.Agent/PromptComposer.cs` — add `{{group_context}}` interpolation
- `Lis.Agent/ConversationService.cs` — pass `isGroup` + agent to `BuildAsync`
- `Lis.Tools/ConfigPlugin.cs` — add `group_context_prompt` to agent config
- `Lis.Tests/Agent/PromptComposerTests.cs` — test interpolation

**Commit:** `✨ feat(agent): add {{group_context}} prompt interpolation`

---

## Task 5: Per-chat debounce configuration

**Problem:** 3s debounce is too short for groups.

**Design:** Add `DebounceMs` to `ChatEntity` (int?, null = global default). In
`MessageDebouncer`, resolve per-chat debounce. Need to load chat config — can reuse the
DB lookup already happening in `ShouldRespondAsync`.

**Files:**
- `Lis.Persistence/Entities/ChatEntity.cs` — add `DebounceMs` (int?)
- `Lis.Agent/MessageDebouncer.cs` — resolve per-chat debounce
- `Lis.Tools/ConfigPlugin.cs` — add `debounce_ms` to get/set

**Commit:** `✨ feat(agent): per-chat debounce configuration`

---

## Task 6: Documentation

- Copy plan to `Plans/group-interaction.md`
- Create `docs/GROUPS.md` — full group interaction docs
- Update `docs/AGENTS.md` — reference group config

**Commit:** `📝 docs: document group interaction model`

---

## DB Changes (Greenfield — rewrite initial migration)

| Column | Table | Type | Default | Task |
|--------|-------|------|---------|------|
| `reply_content` | `message` | `text` | null | 0 |
| `group_context_messages` | `chat` | `integer` | null | 1 |
| `open_group` | `chat` | `boolean` | false | 2 |
| `debounce_ms` | `chat` | `integer` | null | 5 |
| `group_context_prompt` | `agent` | `text` | null | 4 |

Since this is greenfield: add all columns to the existing initial migration entity
definitions, then regenerate the migration.

---

## Edge Cases

| Edge Case | Fix |
|-----------|-----|
| Quoted message is image/media | Show `[image]`, `[image: caption]`, `[audio: transcription]` etc. |
| Quoted message outside current session | Use stored `ReplyContent` — self-contained |
| Truncated/media message AI wants to see | `get_message(id)` tool fetches full content from DB |
| OpenGroup=true + RequireMention=true | Both apply independently: anyone can talk, but must mention |
| Bot JID detection | Extract from `envelope.DeviceId` on every webhook, no startup fetch |
| Group name not captured | Revisit separately — GOWA may send differently for groups |
| Reply-to-bot detection | DB lookup: `WHERE external_id = repliedToId AND is_from_me` |

---

## Verification

1. `dotnet build` after each task
2. `dotnet test Lis.Tests/Lis.Tests.csproj` per task
3. Manual: non-relevant messages windowed correctly in group context
4. Manual: reply-to-bot triggers response when `RequireMention=true`
5. Manual: quoted message text appears in AI context
6. Manual: `{{group_context}}` expands in group, empty in 1-on-1
7. Manual: per-chat debounce works via config tool
8. Manual: `OpenGroup=true` allows anyone, `RequireMention` still gates
9. Manual: `get_message(id)` tool returns full message content
