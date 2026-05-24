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

Extended thinking is configured per-agent via `thinking_effort` (low/medium/high
or a raw budget_tokens integer). The `"display": "summarized"` parameter is
injected so thinking content streams in real-time rather than arriving as a long
pause followed by output.

Thinking content arrives as `thinking_chunk` frames, separate from text `chunk`
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
