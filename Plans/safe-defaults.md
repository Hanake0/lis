# Safe Defaults for New Agents & Chats

## Context

Currently, when the Lis bot is added to a group or receives a new DM, several defaults are too permissive:

1. **Groups don't require mention** (`require_mention = false`) — once authorized, the bot responds to every message, wasting tokens
2. **Non-owner DMs are not disabled** — while auth gates block responses, the chat entity is created `enabled = true`
3. **Config tools are fully open** (`ToolAuthLevel.Open`) — any authorized user can escalate: change `exec_security`, flip `open_group`, add senders, change model
4. **Commands have no auth layer** — any authorized user can `/agent new`, `/agent delete`, `/model`
5. **Agent creation doesn't set security fields explicitly** — relies on implicit entity defaults
6. **No way to manage other chats remotely** — owner can't enable a disabled chat from their own conversation

Goal: lock everything down by default. Owner opens things up conversationally.

---

## Changes

### 1. Harden new chat defaults in UpsertChatAsync

**File**: `Lis.Agent/ConversationService.cs` — `UpsertChatAsync` (~line 360)

Change signature to accept `ownerJid`. Set defaults based on chat type and sender:

```csharp
private static async Task<ChatEntity> UpsertChatAsync(
    LisDbContext db, IncomingMessage message, string ownerJid, CancellationToken ct) {
    // ...
    chat = new ChatEntity {
        ExternalId     = message.ChatId,
        Name           = message.SenderName,
        IsGroup        = message.IsGroup,
        RequireMention = message.IsGroup,
        Enabled        = message.IsGroup || message.SenderId == ownerJid,
        CreatedAt      = DateTimeOffset.UtcNow,
        UpdatedAt      = DateTimeOffset.UtcNow
    };
```

| Chat type | `enabled` | `require_mention` |
|-----------|-----------|-------------------|
| Owner DM | `true` | `false` |
| Non-owner DM | **`false`** | `false` |
| Group | `true` | **`true`** |

Update call site in `IngestMessageAsync` (~line 91) to pass `lisOptions.Value.OwnerJid`. Method becomes non-static.

### 2. Admin tools for remote chat management (OwnerOnly)

**File**: `Lis.Tools/ConfigPlugin.cs`

Add two new tools so the owner can manage any chat from their own conversation:

- **`list_chats`** — `OwnerOnly`. Lists all chats with: name, external_id, is_group, enabled, agent, require_mention, open_group.
- **`manage_chat`** — `OwnerOnly`. Updates config on any chat by external_id. Parameters: `chat_id`, `key`, `value`. Same valid keys as `update_chat_config`.

UX: Owner says "show me all chats" → sees list → "enable the chat with 5511..." → AI calls `manage_chat`.

### 3. Config write tools → OwnerOnly

**File**: `Lis.Tools/ConfigPlugin.cs`

| Tool | Current | New |
|------|---------|-----|
| `get_agent_config` | Open | Open (read-only) |
| `update_agent_config` | Open | **OwnerOnly** |
| `get_chat_config` | Open | Open (read-only) |
| `update_chat_config` | Open | **OwnerOnly** |
| `add_allowed_sender` | Open | **OwnerOnly** |
| `remove_allowed_sender` | Open | **OwnerOnly** |
| `list_allowed_senders` | Open | Open (read-only) |

4 attribute changes. ToolRunner already enforces OwnerOnly at line 118 via `ToolContext.IsOwner`.

### 4. Owner-only slash commands

**File**: `Lis.Agent/Commands/IChatCommand.cs`

Add default property to interface:
```csharp
bool OwnerOnly => false;
```

**File**: `Lis.Agent/ConversationService.cs` — `RespondAsync` (~line 133)

Add owner gate before command execution:
```csharp
if (match.Command.OwnerOnly && message.SenderId != lisOptions.Value.OwnerJid) {
    await channelClient.SendMessageAsync(message.ChatId,
        "⛔ This command requires owner authorization.", message.ExternalId, ct);
    // persist response + return
}
```

**Mark OwnerOnly via interface property**:

| File | Command |
|------|---------|
| `Commands/ModelCommand.cs` | `/model` |
| `Commands/ApproveCommand.cs` | `/approve`, `/deny` |

**Per-subcommand check inside ExecuteAsync** (AgentCommand keeps `OwnerOnly => false`):

| Subcommand | Auth |
|------------|------|
| `/agent` (no args) | Open (read-only) |
| `/agent new`, `/agent delete`, `/agent {switch}` | Owner only — check `ctx.Message.SenderId` vs `lisOptions.Value.OwnerJid` inside ExecuteAsync |

### 5. Explicit security defaults on agent creation

**File**: `Lis.Agent/Commands/AgentCommand.cs` — `CreateAgentAsync` (~line 80)

Add explicit fields to `new AgentEntity`:
```csharp
ExecSecurity = "deny",
ToolProfile  = "standard",
```

Matches current entity defaults but makes the contract explicit and resilient.

---

## Files Modified

| File | Change |
|------|--------|
| `Lis.Agent/ConversationService.cs` | Hardened UpsertChatAsync defaults + ownerJid param + command owner gate in RespondAsync |
| `Lis.Tools/ConfigPlugin.cs` | 4 existing tools → OwnerOnly + 2 new admin tools (list_chats, manage_chat) |
| `Lis.Agent/Commands/IChatCommand.cs` | Add `bool OwnerOnly => false;` to interface |
| `Lis.Agent/Commands/AgentCommand.cs` | Owner check for write subcommands + explicit ExecSecurity/ToolProfile in CreateAgentAsync |
| `Lis.Agent/Commands/ModelCommand.cs` | `OwnerOnly => true` |
| `Lis.Agent/Commands/ApproveCommand.cs` | `OwnerOnly => true` on both classes |

---

## Verification

1. `dotnet build` — no compile errors
2. `dotnet test Lis.Tests/Lis.Tests.csproj` — existing tests pass
3. Manual checks:
   - Non-owner DMs bot → chat created with `enabled = false` in DB
   - Owner DMs bot → chat created with `enabled = true`
   - New group → `require_mention = true` in DB
   - Non-owner tries "change the model" → tool blocked with owner message
   - Non-owner types `/model haiku` → blocked with owner message
   - Non-owner types `/agent` → shows current agent (allowed)
   - Non-owner types `/agent new test` → blocked
   - Owner says "show me all chats" → AI calls `list_chats` → succeeds
   - Owner says "enable the chat with 5511..." → AI calls `manage_chat` → succeeds
   - Owner says "allow everyone in this group" → AI calls `update_chat_config` → succeeds
