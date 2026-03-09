# Multi-Agent System for Lis

## Context

Lis currently operates as a single-agent system: one model config (`ModelSettings`), one global set of prompt sections, and a single authorization rule (`OwnerJid`). Every conversation shares the same AI personality, model, and behavior.

This plan introduces **named agents** (model + prompts + compaction config) and **per-conversation config** (allowed senders, require mention, enabled). Each chat is bound to one agent at a time, and has its own access control — separate from the agent's identity.

---

## Design

### Separation of Concerns

**Agent** = the AI's identity: model, prompt sections, compaction config, tool notifications.

**Chat config** = per-conversation behavior: who can interact, whether mention is required, whether the bot is enabled. These are **not** agent properties — they're conversation properties.

### Authorization Model

Effective authorized senders for a given chat:

```
authorized = {owner (LIS_OWNER_JID)} ∪ {chat's allowed senders}
```

- **Owner** is always authorized everywhere, unconditionally
- **Per-chat allowed senders** — relational table `chat_allowed_sender(chat_id, sender_id)`
- **`chat.enabled`** — master switch to disable the bot for a specific chat
- **`chat.require_mention`** — in groups, only respond when @mentioned
- If no allowed senders and no owner match → no response

---

## 1. New Entity: `AgentEntity`

**File:** `Lis.Persistence/Entities/AgentEntity.cs`

```
Table: "agent"
  id                        long PK
  name                      varchar(64), required, unique  — slug ("default", "research")
  display_name              varchar(128), nullable         — friendly name

  -- Model config
  model                     varchar(128), required         — e.g. "claude-sonnet-4-20250514"
  max_tokens                int, default 4096
  context_budget            int, default 12000
  thinking_effort           varchar(16), nullable          — "low"/"medium"/"high"/token count

  -- Behavior
  tool_notifications        bool, default true

  -- Compaction config (moved from LisOptions to per-agent)
  compaction_threshold      int, default 0                 — 0 = 80% of context_budget
  keep_recent_tokens        int, default 4000
  tool_prune_threshold      int, default 8000
  tool_keep_threshold       int, default 2000
  tool_summarization_policy varchar(16), nullable          — "auto"/"keep_all"/"keep_none"

  is_default                bool, default false
  created_at                timestamptz
  updated_at                timestamptz
```

Note: **No `compaction_model` on agent** — compaction always uses the global compaction model (`LIS_COMPACTION_MODEL` env var / keyed `IChatClient`). If unset globally, falls back to agent's main model.

### Config Defaults from Env

When the default agent is seeded on first startup, all values come from env vars (existing `ANTHROPIC_*` and `LIS_*`). DB values take precedence once set — the AI can modify them via the `ConfigPlugin` tool at runtime.

## 2. New Entity: `ChatAllowedSenderEntity`

**File:** `Lis.Persistence/Entities/ChatAllowedSenderEntity.cs`

```
Table: "chat_allowed_sender"
  id          long PK
  chat_id     long FK → chat.id, CASCADE
  sender_id   varchar(64), required

  Unique index on (chat_id, sender_id)
  Index on chat_id
```

Proper relational table — no JSON arrays. Queryable, indexable, manageable via commands.

## 3. Entity Modifications

### `PromptSectionEntity` — add `AgentId`

```diff
+ agent_id    long, FK → agent.id, CASCADE, required
```

- Drop unique index on `name`
- Add compound unique index on `(agent_id, name)`

### `ChatEntity` — add agent + conversation config

```diff
+ agent_id         long?, FK → agent.id, SET NULL
+ enabled          bool, default true       — master switch for this chat
+ require_mention  bool, default false      — groups: only respond when @mentioned
```

- Navigation: `public AgentEntity? Agent { get; set; }`
- Navigation: `public ICollection<ChatAllowedSenderEntity> AllowedSenders { get; set; } = []`

### `LisDbContext` — add DbSets

```diff
+ public DbSet<AgentEntity>              Agents              { get; init; } = null!;
+ public DbSet<ChatAllowedSenderEntity>  ChatAllowedSenders  { get; init; } = null!;
```

### `LisOptions` — add `NewSessionOnAgentSwitch`

```diff
+ public bool NewSessionOnAgentSwitch { get; init; } = true;
```

Env var: `LIS_NEW_SESSION_ON_AGENT_SWITCH` (default `true`)

## 4. Migration

**Greenfield project — can reset DB.** No data migration SQL needed.

Standard EF Core migration: `dotnet ef migrations add add_agents ...`
Then reset + apply, or auto-migrate on startup.

## 5. New Service: `AgentService`

**File:** `Lis.Agent/AgentService.cs`

### Responsibilities

- **Resolve agent for a chat** — `chat.AgentId` → load, fallback to default
- **Authorization** — merged owner + chat allowed senders
- **Derive ModelSettings** — `AgentEntity` → `ModelSettings`
- **Default agent seeding** — on startup, sync default agent's model from env

### Authorization Logic

```
ShouldRespond(ChatEntity chat, IncomingMessage message, string ownerJid):
  1. if !chat.Enabled → false
  2. if message.SenderId == ownerJid → true (owner always authorized)
  3. if chat.AllowedSenders contains message.SenderId → true
  4. if message.IsGroup && chat.RequireMention → check if mentioned
  5. → false
```

### Key Methods

```csharp
Task<AgentEntity> ResolveForChatAsync(LisDbContext db, ChatEntity chat, CancellationToken ct)
bool ShouldRespond(ChatEntity chat, IncomingMessage message, string ownerJid)
ModelSettings ToModelSettings(AgentEntity agent)
Task SeedDefaultAsync(LisDbContext db, ModelSettings envDefaults, CancellationToken ct)
```

## 6. Service Modifications

### `ConversationService`

- Replace `ModelSettings modelSettings` with `AgentService agentService`
- `IngestMessageAsync` / `RespondAsync`: resolve agent → derive `ModelSettings`
- Replace `ShouldRespond()` with `agentService.ShouldRespond(chat, message, ownerJid)`
- Pass `agent.Id` to `PromptComposer.BuildAsync`
- Set `ToolContext.AgentId = agent.Id`
- Use `agent.ToolNotifications` instead of global `LisOptions.ToolNotifications`
- Pass agent-derived thresholds to `CheckCompactionTriggersAsync`

### `PromptComposer`

- Signature: `BuildAsync(LisDbContext db, long agentId, CancellationToken ct)`
- Filter: `db.PromptSections.Where(s => s.IsEnabled && s.AgentId == agentId)`

### `ToolContext` — add AgentId

```diff
+ private static readonly AsyncLocal<long?> AgentIdLocal = new();
+ public static long? AgentId { get => AgentIdLocal.Value; set => AgentIdLocal.Value = value; }
```

### `PromptPlugin` — agent-scoped queries

All methods filter by `ToolContext.AgentId`:
```csharp
long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
db.PromptSections.Where(s => s.AgentId == agentId)...
```

### `CompactionService`

- Replace `ModelSettings` with `AgentService` — resolve agent from chat, derive model settings
- Use agent-specific compaction thresholds
- Compaction model: use global keyed `IChatClient("compaction")` as today (not agent's model)

### `StatusCommand`

- Read model/budget from `ctx.Agent` instead of `ModelSettings`
- Show agent name in status output

### `CommandContext` — add Agent

```diff
  public sealed record CommandContext(
      IncomingMessage Message,
      ChatEntity      Chat,
      SessionEntity?  Session,
      LisDbContext    Db,
+     AgentEntity     Agent,
      string?         Args = null);
```

### `MessageDebouncer`

- Delegate `ShouldRespond` to `AgentService`

## 7. New Commands

### `/agent [subcommand]`
**File:** `Lis.Agent/Commands/AgentCommand.cs`
- No args → show current agent info (name, model, display name)
- `<name>` → switch chat to named agent. New session if `LIS_NEW_SESSION_ON_AGENT_SWITCH=true`
- `new <name> [display_name]` → create agent, **copy prompt sections from current agent**
- `delete <name>` → delete non-default agent, reassign its chats to default

### `/agents`
**File:** `Lis.Agent/Commands/AgentsCommand.cs`
- List all agents: name, model, chat count, default indicator

### `/model [model_name]`
**File:** `Lis.Agent/Commands/ModelCommand.cs`
- No args → show current agent's model
- `<model>` → update current agent's model in DB

### `/models`
**File:** `Lis.Agent/Commands/ModelsCommand.cs`
- List known Anthropic models (opus-4-6, sonnet-4-6, haiku-4-5, etc.)
- Show which model each agent uses

## 8. New Tool: `ConfigPlugin`

**File:** `Lis.Tools/ConfigPlugin.cs`

Gives the AI the ability to read and modify agent config and chat config at runtime. Follows the same pattern as `PromptPlugin` — uses `IServiceScopeFactory` and `ToolContext`.

### Functions

**`get_agent_config`** — show current agent's full config
- Reads agent via `ToolContext.AgentId`
- Returns: name, display_name, model, max_tokens, context_budget, thinking_effort, tool_notifications, compaction settings

**`update_agent_config(key, value)`** — modify a config value on the current agent
- Validates key against known fields
- Parses value to correct type (int, bool, string)
- Persists to DB
- Returns confirmation

**`get_chat_config`** — show current chat's config
- Reads chat via `ToolContext.ChatId`
- Returns: enabled, require_mention, agent name, allowed senders list

**`update_chat_config(key, value)`** — modify a chat config value
- Supports: `enabled` (bool), `require_mention` (bool)
- Persists to DB

**`add_allowed_sender(sender_id)`** — add sender to current chat's allowed list
- Inserts into `chat_allowed_sender`
- Returns confirmation

**`remove_allowed_sender(sender_id)`** — remove sender from current chat's allowed list
- Deletes from `chat_allowed_sender`
- Returns confirmation

**`list_allowed_senders`** — list current chat's allowed senders
- Queries `chat_allowed_sender` by chat_id

All functions marked `[ToolSummarization(SummarizationPolicy.Prune)]` — outputs pruned during compaction.

### Registration

In `AgentSetup.cs`:
```csharp
kernel.Plugins.AddFromType<ConfigPlugin>(pluginName: "cfg", serviceProvider: sp);
```

## 9. DI Changes

### `AgentSetup.cs`
```diff
+ services.AddSingleton<AgentService>();
+ services.AddSingleton<IChatCommand, AgentCommand>();
+ services.AddSingleton<IChatCommand, AgentsCommand>();
+ services.AddSingleton<IChatCommand, ModelCommand>();
+ services.AddSingleton<IChatCommand, ModelsCommand>();

  // In Kernel builder:
+ kernel.Plugins.AddFromType<ConfigPlugin>(pluginName: "cfg", serviceProvider: sp);
```

### `Program.cs`
- Keep global `ModelSettings` singleton as seed source for default agent
- Add `NewSessionOnAgentSwitch` to `LisOptions`
- After migration, seed default agent: all configs come from env vars initially

### Kernel singleton — no change
Model switching is per-request via `PromptExecutionSettings.ModelId`.

## 10. Implementation Sequence

**Every step ends with a micro-commit** (gitmoji + conventional commits).

### Phase 1: Foundation (entities + migration)

| # | Task | Commit |
|---|------|--------|
| 1 | Create `AgentEntity` with configuration | `✨ feat(persistence): add AgentEntity` |
| 2 | Create `ChatAllowedSenderEntity` | `✨ feat(persistence): add ChatAllowedSenderEntity` |
| 3 | Modify `PromptSectionEntity` — add AgentId FK, compound unique | `♻️ refactor(persistence): scope prompt sections to agent` |
| 4 | Modify `ChatEntity` — add AgentId, Enabled, RequireMention, AllowedSenders nav | `♻️ refactor(persistence): add per-chat config fields` |
| 5 | Update `LisDbContext` — add DbSets | `♻️ refactor(persistence): register new DbSets` |
| 6 | Add `NewSessionOnAgentSwitch` to `LisOptions` | `✨ feat(core): add NewSessionOnAgentSwitch option` |
| 7 | Reset DB + create fresh migration | `🗃️ feat(persistence): add_agents migration` |

### Phase 2: Core Services

| # | Task | Commit |
|---|------|--------|
| 8 | Add `AgentId` to `ToolContext` | `✨ feat(core): add AgentId to ToolContext` |
| 9 | Create `AgentService` | `✨ feat(agent): add AgentService` |
| 10 | Modify `PromptComposer` — accept agentId | `♻️ refactor(agent): scope PromptComposer to agent` |
| 11 | Modify `PromptPlugin` — filter by ToolContext.AgentId | `♻️ refactor(tools): scope PromptPlugin to agent` |
| 12 | Extend `CommandContext` with Agent | `♻️ refactor(agent): add Agent to CommandContext` |

### Phase 3: Refactor Existing Services

| # | Task | Commit |
|---|------|--------|
| 13 | Modify `ConversationService` — per-request agent, chat-based auth | `♻️ refactor(agent): per-request agent resolution` |
| 14 | Modify `MessageDebouncer` — delegate ShouldRespond | `♻️ refactor(agent): delegate auth to AgentService` |
| 15 | Modify `CompactionService` — resolve agent from chat | `♻️ refactor(agent): agent-aware compaction` |
| 16 | Update `StatusCommand` — read from agent | `♻️ refactor(agent): agent-aware status command` |

### Phase 4: New Commands + Tool

| # | Task | Commit |
|---|------|--------|
| 17 | Implement `AgentCommand` | `✨ feat(agent): add /agent command` |
| 18 | Implement `AgentsCommand` | `✨ feat(agent): add /agents command` |
| 19 | Implement `ModelCommand` | `✨ feat(agent): add /model command` |
| 20 | Implement `ModelsCommand` | `✨ feat(agent): add /models command` |
| 21 | Implement `ConfigPlugin` | `✨ feat(tools): add ConfigPlugin` |
| 22 | Register commands + plugin in `AgentSetup.cs` | `♻️ refactor(agent): register new commands and ConfigPlugin` |

### Phase 5: Startup + DI

| # | Task | Commit |
|---|------|--------|
| 23 | Update `Program.cs` — agent seeding, new env vars | `♻️ refactor(api): agent seeding on startup` |

### Phase 6: Documentation

| # | Task | Commit |
|---|------|--------|
| 24 | Copy plan to `Plans/multi-agent.md` | `📝 docs: add multi-agent plan` |
| 25 | Write feature docs to `docs/AGENTS.md` | `📝 docs: add agents documentation` |

### Phase 7: Testing + Cleanup

| # | Task | Commit |
|---|------|--------|
| 26 | Unit tests for `AgentService` | `✅ test(agent): AgentService tests` |
| 27 | Command tests | `✅ test(agent): command tests` |
| 28 | Run `jb cleanupcode` | `🎨 style: cleanup` |

## 11. Verification

1. `dotnet build`
2. Reset DB + apply migration
3. `dotnet test Lis.Tests/Lis.Tests.csproj`
4. Manual testing via WhatsApp:
   - `/status` shows agent name + model
   - `/agents` lists default agent
   - `/agent new research` creates agent with copied prompts
   - `/agent research` switches to research agent
   - `/model claude-opus-4-6` changes model
   - `/models` lists known models
   - Prompt sections are agent-scoped
   - Auth: owner works everywhere, per-chat allowed senders work
   - ConfigPlugin: AI can read/modify agent config and chat config
   - Config defaults from env vars, DB overrides take precedence

## 12. Critical Files

| File | Action |
|------|--------|
| `Lis.Persistence/Entities/AgentEntity.cs` | **Create** |
| `Lis.Persistence/Entities/ChatAllowedSenderEntity.cs` | **Create** |
| `Lis.Persistence/Entities/PromptSectionEntity.cs` | **Modify** — add AgentId FK |
| `Lis.Persistence/Entities/ChatEntity.cs` | **Modify** — add AgentId, Enabled, RequireMention |
| `Lis.Persistence/LisDbContext.cs` | **Modify** — add DbSets |
| `Lis.Core/Configuration/LisOptions.cs` | **Modify** — add NewSessionOnAgentSwitch |
| `Lis.Core/Util/ToolContext.cs` | **Modify** — add AgentId |
| `Lis.Agent/AgentService.cs` | **Create** |
| `Lis.Agent/ConversationService.cs` | **Modify** — per-request agent, chat-based auth |
| `Lis.Agent/PromptComposer.cs` | **Modify** — filter by agentId |
| `Lis.Agent/CompactionService.cs` | **Modify** — resolve agent |
| `Lis.Agent/AgentSetup.cs` | **Modify** — register services/commands/plugins |
| `Lis.Agent/Commands/IChatCommand.cs` | **Modify** — add Agent to CommandContext |
| `Lis.Agent/Commands/StatusCommand.cs` | **Modify** — read from agent |
| `Lis.Agent/Commands/AgentCommand.cs` | **Create** |
| `Lis.Agent/Commands/AgentsCommand.cs` | **Create** |
| `Lis.Agent/Commands/ModelCommand.cs` | **Create** |
| `Lis.Agent/Commands/ModelsCommand.cs` | **Create** |
| `Lis.Tools/PromptPlugin.cs` | **Modify** — agent-scoped queries |
| `Lis.Tools/ConfigPlugin.cs` | **Create** — agent/chat config tool |
| `Lis.Api/Program.cs` | **Modify** — seeding, env vars |
| `Plans/multi-agent.md` | **Create** — plan document |
| `docs/AGENTS.md` | **Create** — feature documentation |
