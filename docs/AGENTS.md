# Agents & Per-Chat Config

## Overview

Lis supports multiple **named agents** — switchable AI profiles that bundle model config, prompt sections, and compaction settings. Each chat (conversation) is bound to one agent at a time and has its own access control config.

**Agent** = the AI's identity (model, prompts, compaction).
**Chat config** = per-conversation behavior (allowed senders, require mention, enabled).

## Agent Config

Each agent has:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `name` | string | required | Unique slug (e.g. "default", "research") |
| `display_name` | string? | null | Friendly name |
| `model` | string | required | Anthropic model ID |
| `max_tokens` | int | 4096 | Max output tokens per response |
| `context_budget` | int | 12000 | Context window budget |
| `thinking_effort` | string? | null | "low", "medium", "high", or token count |
| `tool_notifications` | bool | true | Send tool execution notifications |
| `compaction_threshold` | int | 0 | 0 = 80% of context_budget |
| `keep_recent_tokens` | int | 4000 | Tokens to keep when compacting |
| `tool_prune_threshold` | int | 8000 | Prune tool outputs above this |
| `tool_keep_threshold` | int | 2000 | Keep recent tools within this |
| `tool_summarization_policy` | string? | null | "auto", "keep_all", "keep_none" |
| `is_default` | bool | false | Default agent for new chats |

The default agent is seeded from environment variables on first startup.

## Chat Config

Each chat has:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `agent_id` | FK? | null | Current agent (null = default) |
| `enabled` | bool | true (groups/owner DMs), false (non-owner DMs) | Master switch for this chat |
| `require_mention` | bool | true (groups), false (DMs) | Require @mention in groups |

### Allowed Senders

Per-chat allowed senders are stored in the `chat_allowed_sender` table (relational, not JSON).

## Authorization

```
authorized = {owner (LIS_OWNER_JID)} ∪ {chat's allowed senders}
```

1. Owner is always authorized everywhere
2. Per-chat allowed senders grant access to specific chats
3. `chat.enabled = false` disables the bot entirely for that chat
4. `chat.require_mention = true` requires @mention in groups

## Commands

| Command | Description | Auth |
|---------|-------------|------|
| `/agent` | Show current agent | Open |
| `/agent <name>` | Switch to named agent | OwnerOnly |
| `/agent new <name> [display]` | Create agent (copies prompts) | OwnerOnly |
| `/agent delete <name>` | Delete non-default agent | OwnerOnly |
| `/agents` | List all agents | Open |
| `/model` | Show current model | OwnerOnly |
| `/model <name>` | Change model | OwnerOnly |
| `/models` | List known models | Open |
| `/approve <id>` | Approve exec request | OwnerOnly |
| `/deny <id>` | Deny exec request | OwnerOnly |

## AI Tools (ConfigPlugin)

The AI can read and modify configs at runtime. Write operations are restricted to the owner.

| Tool | Description | Auth |
|------|-------------|------|
| `cfg_get_agent_config` | Show current agent config | Open |
| `cfg_update_agent_config(key, value)` | Modify agent config | OwnerOnly |
| `cfg_get_chat_config(chatId?)` | Show chat config (any chat or current) | Open |
| `cfg_update_chat_config(key, value, chatId?)` | Modify chat config (any chat or current) | OwnerOnly |
| `cfg_add_allowed_sender(id, chatId?)` | Allow sender in chat | OwnerOnly |
| `cfg_remove_allowed_sender(id, chatId?)` | Remove sender from chat | OwnerOnly |
| `cfg_list_allowed_senders(chatId?)` | List allowed senders | Open |
| `cfg_list_chats` | List all chats with config | OwnerOnly |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `LIS_NEW_SESSION_ON_AGENT_SWITCH` | `true` | Start new session when switching agents |
| `LIS_OWNER_JID` | "" | Owner JID (always authorized) |

Agent config defaults come from existing `ANTHROPIC_*` and `LIS_*` env vars. Once the default agent is seeded, DB values take precedence.
