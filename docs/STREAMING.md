# Streaming & Real-Time Features

Lis supports token-level streaming of AI responses, thinking content, and tool
call visibility through the web channel. WhatsApp receives complete messages
(no streaming support in the protocol).

## How It Works

```
ToolRunner (streaming loop)
  │
  ├─ thinking chunk → IChannelClient.SendThinkingChunkAsync
  ├─ text chunk     → IChannelClient.SendChunkAsync
  ├─ tool start     → IChannelClient.SendToolStartAsync
  ├─ tool result    → IChannelClient.SendToolResultAsync
  │
  └─ final message  → IChannelClient.SendMessageAsync (source of truth)
```

`IChannelClient` defines all streaming methods with default no-op implementations.
Channels that support streaming (web) override them; channels that don't (WhatsApp)
inherit the no-ops. The architecture is channel-agnostic — `ToolRunner` and
`ConversationService` never reference a specific channel.

## Streaming Frames (Server → Client)

| Frame              | Fields                                          | Purpose                           |
| ------------------ | ----------------------------------------------- | --------------------------------- |
| `chunk`            | `chat_id`, `id`, `delta`, `done`                | Text token delta                  |
| `thinking_chunk`   | `chat_id`, `id`, `delta`, `done`                | Thinking/reasoning delta          |
| `tool_start`       | `chat_id`, `id`, `tool_name`, `tool_args`       | Tool invocation begins            |
| `tool_result`      | `chat_id`, `id`, `tool_name`, `result`, `error` | Tool invocation completed         |

After streaming completes, the server still sends the full `message` frame with
the complete text and token usage. The client uses this as the source of truth,
replacing any streamed content. This ensures correctness even if chunks were lost
or arrived out of order.

## Thinking

Extended thinking is configured per-agent via `thinking_effort`. Five levels
match Claude Code's behavior: `low | medium | high | xhigh | max`. A raw integer
is also accepted as the budget_tokens value (budget mode only).

Two thinking modes:

- **budget** — `{type: "enabled", budget_tokens: N, display: "summarized"}`. Fixed
  budget. Default for Sonnet/Haiku and older Opus models. Deprecated on Claude
  4.6 but still functional. Required for `xhigh` effort (no adaptive equivalent).
- **adaptive** — `{type: "adaptive", display: "summarized", output_config: {effort: <level>}}`.
  Model decides how much to think within the effort budget. **Required for Opus 4.7+**
  (budget mode is removed on that model). `output_config.effort` accepts only
  `low/medium/high/max` — `xhigh` falls back to `high` in adaptive.

Budget values per effort (budget mode):

| Effort | Budget tokens         |
| ------ | --------------------- |
| low    | 1024                  |
| medium | 4096                  |
| high   | 16000                 |
| xhigh  | 32000                 |
| max    | Opus 64000, Sonnet 32000, Haiku 8192 |

The `"display": "summarized"` parameter is set on both modes so thinking content
streams in real-time. Without it, thinking arrives as a long pause then output.

Per-message overrides on the `message` client frame:

- `effort` — override the agent's effort level
- `thinking_mode` — `"budget" | "adaptive" | "auto"` (auto = adaptive on Opus 4.7+, budget otherwise)

Thinking content arrives as `thinking_chunk` frames separate from text `chunk`
frames. The client renders thinking in a collapsible block above the response
text, collapsed by default after the response completes.

## Token Usage

Token counts travel on the final `message` frame and on `MessageRow` in history:

- `input_tokens` — prompt tokens
- `output_tokens` — response tokens (includes thinking tokens on some models)
- `cache_read_tokens` — prompt cache hits
- `cache_creation_tokens` — prompt cache writes
- `thinking_tokens` — extended thinking tokens

## Per-Message Overrides

The client can override model settings on a per-message basis by including
optional fields on the `message` client frame:

| Field          | Effect                                   |
| -------------- | ---------------------------------------- |
| `model`        | Override the agent's model for this turn  |
| `effort`       | Override thinking effort                  |
| `tool_profile` | Override tool profile (minimal/standard/coding/full) |

Overrides are stateless — they travel with the message, not stored on the server.
The agent entity is unchanged unless the client explicitly sends `save_overrides`.
