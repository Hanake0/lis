# Response Directives

Response directives give the AI model control over how its responses are delivered on WhatsApp.

## Capabilities

### 1. Quote Control (`[QUOTE]` prefix)

By default, messages are sent as plain messages (no quote/reply). The model can start its response with `[QUOTE]` to quote the user's message:

```
[QUOTE]
Yes, that's exactly right!
```

The `[QUOTE]` prefix is stripped before sending. Use sparingly — only when referencing a specific message.

### 2. No Response (`NO_RESPONSE`)

The model can output `NO_RESPONSE` as its entire response to suppress sending a text message. Useful after reacting with an emoji to just acknowledge without replying:

1. Model calls `react_to_message(emoji: "👍")`
2. Model outputs `NO_RESPONSE`
3. User sees a thumbs up reaction, no text reply

### 3. React Tool (`react_to_message`)

React to any message with an emoji. Supports targeting by internal message ID.

**Parameters:**
- `emoji` (required) — The emoji to react with (e.g. '👍', '❤️', '😂')
- `messageId` (optional) — Internal message ID to react to. Defaults to the latest message.

## Message IDs in Context Window

User messages in the context window are prefixed with their internal DB ID and sender name:

```
[42] Alice: hello there
[43] Bob: what's up?
```

Format: `[id] Name: message`

The model can use these IDs to target reactions to specific messages. Internal IDs are short numbers, much cheaper in tokens than external WhatsApp IDs.

## Implementation

- **Parser**: `Lis.Agent/ResponseDirectives.cs` — parses `[QUOTE]` and `NO_RESPONSE`
- **React tool**: `Lis.Tools/ResponsePlugin.cs` — `react_to_message` Semantic Kernel function
- **Context prefix**: `Lis.Agent/ContextWindowBuilder.cs` — `UserPrefix()` helper
- **Response loop**: `Lis.Agent/ConversationService.cs` — applies directives before sending
- **Prompt section**: DB migration seeds `response_format` prompt section for all agents
