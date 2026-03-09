# Response Control: Quote Choice + NO_RESPONSE + React Tool

## Context

Currently in `ConversationService.cs:188`, every AI response is sent as a WhatsApp reply/quote to the user's message (`message.ExternalId` is always passed as `replyToId`). This makes every response look like a quoted reply, which is noisy in a 1-on-1 conversation. Additionally, the model has no way to "just acknowledge" a message (e.g., react with a thumbs up) without sending a text response, and it can't target reactions to specific messages.

Inspired by OpenClaw's pattern: the model can react to specific messages by ID, and output `NO_RESPONSE` to skip sending text.

**Goal**: Give the AI model three new capabilities:
1. **Quote control** - Choose whether to quote/reply or send a plain message (default: no quote)
2. **NO_RESPONSE** - Opt out of sending a text response entirely
3. **React with targeting** - React to any message by its internal ID (compact, token-efficient)

## Implementation Tasks

Each task gets its own micro-commit after completion.

---

### Task 1: Prefix user messages with internal ID + sender name in context window
**File**: [ContextWindowBuilder.cs](Lis.Agent/ContextWindowBuilder.cs)

At line 96-100 where plain user messages are added, prefix with internal DB ID and sender name (inspired by OpenClaw's `SenderName: message [id:123]` pattern, but more compact):

```csharp
// Before:
string content = msg.Body ?? msg.MediaCaption ?? "[media]";
if (msg.IsFromMe) history.AddAssistantMessage(content);
else              history.AddUserMessage(content);

// After:
string body = msg.Body ?? msg.MediaCaption ?? "[media]";
if (msg.IsFromMe) {
    history.AddAssistantMessage(body);
} else {
    string prefix = msg.SenderName is { Length: > 0 } name
        ? $"[{msg.Id}] {name}: "
        : $"[{msg.Id}] ";
    history.AddUserMessage(prefix + body);
}
```

Result format:
- Group: `[42] Alice: hello there`
- 1-on-1 (with name): `[42] Hanake: hello there`
- Fallback (no name): `[42] hello there`

Internal IDs are short numbers — much cheaper in tokens than external WhatsApp IDs. Sender name enables the model to distinguish speakers in group chats.

Only prefix **user messages** (not assistant/tool messages).

Also apply the same prefix pattern to the **image/media branch** (line 88-95) for user media messages.

**Commit**: `✨ feat(agent): prefix user messages with internal ID and sender name`

---

### Task 2: Add `MessageExternalId` to ToolContext
**File**: [ToolContext.cs](Lis.Core/Util/ToolContext.cs)

Add a new `AsyncLocal<string?>` for the triggering message's external ID (fallback for react tool):
```csharp
private static readonly AsyncLocal<string?> MessageExternalIdLocal = new();
public static string? MessageExternalId { get => MessageExternalIdLocal.Value; set => MessageExternalIdLocal.Value = value; }
```

Set it in `ConversationService.RespondAsync` (line ~159) alongside `ChatId` and `Channel`:
```csharp
ToolContext.MessageExternalId = message.ExternalId;
```

**Commit**: `✨ feat(core): add MessageExternalId to ToolContext`

---

### Task 3: Create `ResponsePlugin` with react tool
**File**: [ResponsePlugin.cs](Lis.Tools/ResponsePlugin.cs) (new)

Semantic Kernel plugin with `react_to_message`:
```csharp
[KernelFunction("react_to_message")]
[Description("React to a message with an emoji. Optionally target a specific message by its ID (the number in brackets before the message).")]
public async Task<string> ReactToMessageAsync(
    [Description("Emoji to react with (e.g. '👍', '❤️', '😂')")]
    string emoji,
    [Description("Optional message ID to react to (e.g. 42). Defaults to the latest message.")]
    long? message_id = null)
```

Logic:
- If `message_id` is provided → look up `ExternalId` from DB via `IServiceScopeFactory` + `LisDbContext`
- If `message_id` is null → fall back to `ToolContext.MessageExternalId`
- Call `ToolContext.Channel.ReactAsync(externalId, ToolContext.ChatId, emoji)`
- Return success/failure message

**Register in** [AgentSetup.cs](Lis.Agent/AgentSetup.cs) line 30:
```csharp
kernel.Plugins.AddFromType<ResponsePlugin>(pluginName: "resp", serviceProvider: sp);
```

**Commit**: `✨ feat(tools): add react_to_message tool with message targeting`

---

### Task 4: Add `ResponseDirectives` parser
**File**: [ResponseDirectives.cs](Lis.Agent/ResponseDirectives.cs) (new)

Static class that parses directives from the model's text:
```csharp
public static (string? Content, bool ShouldQuote) Parse(string? raw)
```

Rules:
- If trimmed content is `NO_RESPONSE` → `(null, false)`
- If content starts with `[QUOTE]\n` or `[QUOTE] ` → strip prefix, `(cleaned, true)`
- Otherwise → `(raw, false)` (default: no quote)

**Commit**: `✨ feat(agent): add ResponseDirectives parser for quote/no-response control`

---

### Task 5: Apply directives in ConversationService response loop
**File**: [ConversationService.cs](Lis.Agent/ConversationService.cs)

Update lines 185-193:
```csharp
await foreach (ChatMessageContent msg in toolRunner.RunAsync(...)) {
    string? externalId = null;
    if (msg.Role == AuthorRole.Assistant) {
        (string? content, bool shouldQuote) = ResponseDirectives.Parse(msg.Content);
        if (content is not null)
            externalId = await channelClient.SendMessageAsync(
                message.ChatId, content, shouldQuote ? message.ExternalId : null, ct);
        // Update msg.Content for persistence (strip directives)
    }
    TokenUsage? msgUsage = ToolRunner.GetUsage(msg);
    if (msgUsage is not null) lastUsage = msgUsage;
    await PersistSkMessageAsync(db, chat, session, msg, msgUsage, externalId, ct);
}
```

Persist **cleaned** content (without `[QUOTE]`/`NO_RESPONSE` directives) to DB so chat history stays clean.

**Commit**: `✨ feat(agent): apply response directives in conversation loop`

---

### Task 6: Add prompt section via DB migration
**Action**: EF Core migration that inserts a `response_format` prompt section.

Content:
```
Response format:
- Messages are sent as plain messages by default (not quoting/replying)
- Start your message with [QUOTE] to reply/quote the user's message (use only when referencing a specific message)
- Output only NO_RESPONSE to skip sending a text message (use after reacting with an emoji to just acknowledge)
- Use the react_to_message tool to react with an emoji — you can target any message by its ID (the number in brackets, e.g. [42] Alice: hello → message_id=42)
- In group chats, each user message shows as [id] Name: message — use the name to address the right person
```

High sort order so it appears at the end of the system prompt.

**Commit**: `✨ feat(persistence): add response_format prompt section migration`

---

### Task 7: Documentation
**File**: [docs/RESPONSE_DIRECTIVES.md](docs/RESPONSE_DIRECTIVES.md) (new)

Document:
- The three capabilities (quote control, NO_RESPONSE, react tool)
- How message IDs work in context window
- How the directive parser works
- Examples

**File**: [plans/response-control.md](plans/response-control.md) (new)

Copy of this plan for historical reference.

**Commit**: `📝 docs: add response directives documentation`

---

## Files Summary

| File | Action |
|------|--------|
| `Lis.Agent/ContextWindowBuilder.cs` | Prefix user messages with internal IDs |
| `Lis.Core/Util/ToolContext.cs` | Add `MessageExternalId` property |
| `Lis.Agent/ConversationService.cs` | Set ToolContext, apply directives in response loop |
| `Lis.Tools/ResponsePlugin.cs` | New - react tool with message targeting |
| `Lis.Agent/ResponseDirectives.cs` | New - directive parser |
| `Lis.Agent/AgentSetup.cs` | Register ResponsePlugin |
| `Lis.Persistence/Migrations/` | New migration - insert prompt section |
| `docs/RESPONSE_DIRECTIVES.md` | New - documentation |
| `plans/response-control.md` | New - plan archive |

## Verification

1. `dotnet build` — compilation check after each task
2. `dotnet test Lis.Tests/Lis.Tests.csproj` — run existing tests
3. Manual test: send messages and verify:
   - User messages show `[42] Name: hello` format in model context
   - Default response is NOT quoted
   - Model can react with emoji via tool (targeting latest or specific message by ID)
   - Model can output `NO_RESPONSE` to skip text
   - Model can use `[QUOTE]` prefix to quote when appropriate
