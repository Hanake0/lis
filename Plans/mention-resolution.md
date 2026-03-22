# Bidirectional @mention resolution (on-demand DB queries)

## Context

WhatsApp mentions arrive as `@552731911808` in message bodies. The AI can't understand who was mentioned. When the AI writes `@Alice`, it needs to become `@phone` so gowa creates a native WhatsApp @mention.

## Design

**On-demand DB queries** against the existing `messages` table (which stores `sender_id` + `sender_name`). No caching, no new tables. Fallback: if a phone/name can't be resolved, leave it as-is.

### Resolution chain (incoming: phone → name)
1. Bot self: `_botJid` phone → `_botDisplayName` (static, always known)
2. DB: query latest `sender_name` for `sender_id LIKE phone%` in that chat
3. Fallback: leave `@phone` as-is

### Resolution chain (outgoing: name → phone)
1. Bot self: `_botDisplayName` → `_botJid` phone
2. Current sender: if name matches `IncomingMessage.SenderName`, use `IncomingMessage.SenderId`
3. DB: query latest `sender_id` for `sender_name = name` in that chat (case-insensitive)
4. Fallback: leave `@name` as-is (readable text, just no native @mention)

### Handling duplicate names
The `ResolveNameToPhoneAsync` method accepts an optional `preferSenderId` parameter. If the name matches and the preferred sender's name equals the query, return that sender's phone first. Otherwise fall back to most recent in chat.

## Critical files
- `lis/Lis.Core/Channel/IConversationService.cs` — resolve method signatures
- `lis/Lis.Agent/ConversationService.cs` — DB queries + outgoing denormalization
- `lis/Lis.Channels/WhatsApp/GowaWebhookController.cs` — incoming normalization + `_botDisplayName`
- `lis/Lis.Persistence/Entities/MessageEntity.cs` — composite indexes on `(sender_id, id)` and `(sender_name, id)`

## DB indexes
Composite indexes support efficient phone→name and name→phone lookups with `ORDER BY id DESC LIMIT 1`:
- `(sender_id, id)`
- `(sender_name, id)`
