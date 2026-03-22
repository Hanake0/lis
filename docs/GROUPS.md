# Group Interaction

Lis supports group chats with fine-grained control over authorization, context building,
and response behavior.

## Authorization Flow

When a message arrives in a group, `AgentService.ShouldRespond` evaluates:

```
1. chat.Enabled == false                          → deny
2. IsGroup && RequireMention && !IsBotMentioned   → deny (applies to everyone, including owner)
3. sender == OwnerJid                             → allow (owner bypasses authorization)
4. sender in AllowedSenders OR chat.OpenGroup      → allow
5. → deny
```

### Flags

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `enabled` | bool | true (groups), false (non-owner DMs) | Master switch — disables all responses |
| `open_group` | bool | false | Allow all group members (skip AllowedSenders check) |
| `require_mention` | bool | true (groups), false (DMs) | Require bot mention to respond |

`OpenGroup` and `RequireMention` are independent axes:
- `OpenGroup=true, RequireMention=false` → anyone can talk freely
- `OpenGroup=true, RequireMention=true` → anyone can talk, but must mention the bot
- `OpenGroup=false, RequireMention=true` → only AllowedSenders, and they must mention

### Mention Detection

Mentions are detected in two layers:

**Layer 1 — Webhook controller** (`GowaWebhookController`):
- **Native @mention** — GOWA includes `mentioned_jids` in webhook extensions. The controller
  checks if the bot's JID is in the list. The bot JID is learned from echo messages (`IsFromMe`)
  or lazily fetched from the GOWA `/app/devices` API on the first webhook after startup.

**Layer 2 — AgentService** (`DetectMentionAsync`):
- **Reply-to-bot** — if the user replies to a bot message, it's treated as an implicit mention
  (DB lookup: `WHERE external_id = repliedToId AND is_from_me = true`)
- **Text pattern** — if the message body contains the bot's display name (case-insensitive).
  The display name comes from `AgentEntity.DisplayName` (e.g., "Lis").

All callers use `AgentService.ShouldRespondAsync` which runs mention detection before the gate
check, ensuring consistent behavior across normal and queued message paths.

## Context Windowing

In group chats, non-relevant messages (noise from senders the bot didn't respond to) are
windowed to reduce token usage and context pollution.

**Algorithm:** Keep all bot messages (assistant/tool). For consecutive non-bot messages
before each bot response, keep only the last N. N is configurable per-chat via
`group_context_messages` (default: 5 from `LIS_GROUP_CONTEXT_MESSAGES`).

### Configuration

| Key | Type | Default | Scope |
|-----|------|---------|-------|
| `group_context_messages` | int | 5 | per-chat (nullable → global default) |
| `LIS_GROUP_CONTEXT_MESSAGES` | env | 5 | global |

## Reply/Quote Context

When a user replies to (quotes) a message, the quoted text is included inline in the
AI context:

```
[42] Alice (replying to Bob: "The meeting is at 3pm"): I agree
[43] Alice (replying to Bob: [image: sunset]): nice!
[44] Alice (replying to Bob: [audio: <transcript>]): ok
```

- Quoted text is truncated to 500 characters
- Media quotes show `[mediaType]` or `[mediaType: caption]`
- Audio quotes include transcription when available
- Sender name resolved from loaded messages when available

## Group Metadata

Group name and topic are fetched from the GOWA API (`GetGroupInfoAsync`) on each incoming
message and cached in-memory with a 1-hour TTL. These are stored on `ChatEntity` and
injected into the system prompt via the `{{chat_context}}` interpolation variable.

## Prompt Interpolation Variables

| Variable | Group chat | 1-on-1 chat |
|----------|-----------|-------------|
| `{{group_context}}` | Group behavioral instructions (custom or default) | Empty string |
| `{{chat_context}}` | `Group: <name>` + `Topic: <topic>` (if set) | Empty string |
| `{{datetime}}` | Current date/time/period in agent timezone | Same |

### `{{group_context}}` — behavioral instructions

**Default expansion:**
> You are in a group chat with multiple participants. Their names appear as prefixes on
> messages. Be concise and natural. Address people by name when relevant. Not every
> message requires a response — use NO_RESPONSE when a message isn't directed at you
> or doesn't need your input. When quoting is appropriate, use [QUOTE] to reply to the
> specific message.

**Customization:** Set `group_context_prompt` on the agent via `update_agent_config`.
When null, uses the hardcoded default.

### `{{chat_context}}` — group metadata

Expands to the group name and topic (from GOWA API). Example:
```
Group: Família
Topic: Grupo da família — só coisas importantes
```
Empty for 1-on-1 chats or when no group name is available.

## Per-Chat Debounce

Each chat can override the global debounce delay (`LIS_MESSAGE_DEBOUNCE_MS`, default 3000ms).
Set via `update_chat_config debounce_ms <value>`. Useful for groups where a longer debounce
(5-10s) lets the bot wait for a natural pause before responding.

## Configuration Reference

### Chat Config (via `update_chat_config`)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true (groups) | Master switch |
| `require_mention` | bool | true (groups) | Require mention to respond |
| `open_group` | bool | false | Allow all group members |
| `group_context_messages` | int | null | Context window size (null = global) |
| `debounce_ms` | int | null | Debounce delay (null = global) |

### Agent Config (via `update_agent_config`)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `group_context_prompt` | text | null | Custom `{{group_context}}` expansion |
