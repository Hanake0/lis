# Group Interaction

Lis supports group chats with fine-grained control over authorization, context building,
and response behavior.

## Authorization Flow

When a message arrives in a group, `AgentService.ShouldRespond` evaluates:

```
1. chat.Enabled == false  → deny
2. sender == OwnerJid     → allow (owner always bypasses)
3. sender in AllowedSenders OR chat.OpenGroup → authorized
4. Not authorized         → deny
5. chat.RequireMention && !IsBotMentioned → deny
6. → allow
```

### Flags

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `enabled` | bool | true | Master switch — disables all responses |
| `open_group` | bool | false | Allow all group members (skip AllowedSenders check) |
| `require_mention` | bool | false | Require bot mention to respond |

`OpenGroup` and `RequireMention` are independent axes:
- `OpenGroup=true, RequireMention=false` → anyone can talk freely
- `OpenGroup=true, RequireMention=true` → anyone can talk, but must mention the bot
- `OpenGroup=false, RequireMention=true` → only AllowedSenders, and they must mention

### Mention Detection

Since GOWA doesn't include `mentioned_jids` in webhook payloads, mentions are detected by
`AgentService.DetectMentionAsync` — the single source of truth for all detection strategies:

1. **Reply-to-bot** — if the user replies to a bot message, it's treated as an implicit mention
   (DB lookup: `WHERE external_id = repliedToId AND is_from_me = true`)
2. **Text pattern** — if the message body contains the bot's display name (case-insensitive).
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

## Group-Aware System Prompt

The `{{group_context}}` interpolation variable in prompt sections expands to group-specific
instructions when the chat is a group, and to empty string for 1-on-1 chats.

**Default expansion:**
> You are in a group chat with multiple participants. Their names appear as prefixes on
> messages. Be concise and natural. Address people by name when relevant. Not every
> message requires a response — use NO_RESPONSE when a message isn't directed at you
> or doesn't need your input.

**Customization:** Set `group_context_prompt` on the agent via `update_agent_config`.
When null, uses the hardcoded default.

## Per-Chat Debounce

Each chat can override the global debounce delay (`LIS_MESSAGE_DEBOUNCE_MS`, default 3000ms).
Set via `update_chat_config debounce_ms <value>`. Useful for groups where a longer debounce
(5-10s) lets the bot wait for a natural pause before responding.

## Configuration Reference

### Chat Config (via `update_chat_config`)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true | Master switch |
| `require_mention` | bool | false | Require mention to respond |
| `open_group` | bool | false | Allow all group members |
| `group_context_messages` | int | null | Context window size (null = global) |
| `debounce_ms` | int | null | Debounce delay (null = global) |

### Agent Config (via `update_agent_config`)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `group_context_prompt` | text | null | Custom `{{group_context}}` expansion |
