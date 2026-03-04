# Store External ID on Sent Messages + Backfill from Echo Webhook

## Context

Agent-sent messages are persisted with `ExternalId = null` and `SenderId = "me"`. Two fixes:

1. **On send**: `IChannelClient.SendMessageAsync` already returns the external message ID — capture it and store it on the `MessageEntity`.
2. **On webhook echo**: Gowa echoes back every sent message (`is_from_me = true`). Currently dropped in the webhook controller. Instead, match by `external_id` and backfill `sender_id` (actual JID), `sender_name`, and `timestamp` (actual WhatsApp timestamp).

No schema changes needed — all columns already exist.

---

## Step 1 — Capture external ID on send

**`Lis.Agent/ConversationService.cs`** — `RespondAsync`

Capture return value from `SendMessageAsync`, pass to `PersistSkMessageAsync`:

```csharp
string? externalId = null;
if (msg.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(msg.Content))
    externalId = await channelClient.SendMessageAsync(message.ChatId, msg.Content, message.ExternalId, ct);
await PersistSkMessageAsync(db, chat, session, msg, msgUsage, externalId, ct);
```

`PersistSkMessageAsync` — add `string? externalId` parameter, set `ExternalId = externalId` on entity.

## Step 2 — Process echo webhooks

Uses `IncomingMessage` as the DTO — same object used for incoming messages, reused for echoes.

**`Lis.Core.Channel.IConversationService`**:
```csharp
Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct);
```

**`Lis.Agent/MessageDebouncer.cs`** — pass through (no debouncing):
```csharp
public async Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct) {
    using IServiceScope scope = scopeFactory.CreateScope();
    ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
    await svc.HandleSentEchoAsync(echo, ct);
}
```

**`Lis.Agent/ConversationService.cs`** — match by `external_id`, update fields:
```csharp
public async Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct) {
    using IServiceScope scope = scopeFactory.CreateScope();
    LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

    MessageEntity? msg = await db.Messages
        .FirstOrDefaultAsync(m => m.ExternalId == echo.ExternalId, ct);
    if (msg is null) return;

    msg.SenderId   = echo.SenderId;
    msg.SenderName = echo.SenderName;
    msg.Timestamp  = echo.Timestamp;
    await db.SaveChangesAsync(ct);
}
```

**`Lis.Channels/WhatsApp/GowaWebhookController.cs`** — build `IncomingMessage` first, then route:
```csharp
// Echoes → backfill sender info
if (payload.IsFromMe) {
    _ = Task.Run(async () => {
        try {
            await conversationService.HandleSentEchoAsync(message, CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing echo {MessageId}", payload.Id);
        }
    });
    return this.Ok();
}

// Empty body → nothing to process
if (string.IsNullOrEmpty(payload.Body))
    return this.Ok();

// Normal incoming → handle
_ = Task.Run(async () => { ... });
```

---

## Multi-channel scalability

Both steps are channel-agnostic:
- `IChannelClient.SendMessageAsync` returns `Task<string?>` — any channel returns its external ID
- `IConversationService.HandleSentEchoAsync` takes `IncomingMessage` — any channel that echoes can call it
- `MessageEntity.ExternalId` is `varchar(128)` — works for any ID format

---

## Files

| File | Change |
|------|--------|
| `Lis.Agent/ConversationService.cs` | Capture `SendMessageAsync` return, pass to persist; add `HandleSentEchoAsync` |
| `Lis.Core/Channel/IConversationService.cs` | Add `HandleSentEchoAsync(IncomingMessage, CancellationToken)` |
| `Lis.Agent/MessageDebouncer.cs` | Pass through `HandleSentEchoAsync` |
| `Lis.Channels/WhatsApp/GowaWebhookController.cs` | Handle `is_from_me` echoes |

## Verify

- `dotnet build` clean
- Send a message → DB row has `external_id` from API response
- Webhook echo arrives → same row updated with actual `sender_id`, `sender_name`, `timestamp`
- Incoming user messages still processed normally (no regression)
