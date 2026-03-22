# Add `mention_triggers` field to AgentEntity

## Context

Text mention detection currently uses `agent.DisplayName` as the regex pattern. This is wrong — `DisplayName` is for display (e.g., "Lis - Personal Assistant") while the mention trigger should be a short keyword (e.g., "lis"). A separate field allows multiple comma-separated triggers (e.g., "lis,liszinha") for flexibility.

## Changes

### 1. Add column to entity
**File:** `lis/Lis.Persistence/Entities/AgentEntity.cs`

Add after `DisplayName`:
```csharp
[Column("mention_triggers", TypeName = "varchar(256)")]
[JsonPropertyName("mention_triggers")]
public string? MentionTriggers { get; set; }
```

### 2. Add EF migration
```bash
dotnet ef migrations add add_mention_triggers --project Lis.Persistence/Lis.Persistence.csproj --startup-project Lis.Api/Lis.Api.csproj
```

### 3. Update mention detection
**File:** `lis/Lis.Agent/AgentService.cs` — `DetectMentionAsync`, Strategy 2

Change from:
```csharp
if (agent.DisplayName is { Length: > 0 } botName
    && Regex.IsMatch(body, $@"\b{Regex.Escape(botName)}\b", RegexOptions.IgnoreCase))
    message.IsBotMentioned = true;
```

To: parse `MentionTriggers` (comma-separated), fall back to `DisplayName` if null/empty, check each trigger with `\b` word boundary regex.

### 4. Seed default agent with triggers
**File:** `lis/Lis.Agent/AgentService.cs` — `SeedDefaultAsync`

Add `MentionTriggers = "lis"` to the default agent creation.

### 5. Expose in config tool
**File:** `lis/Lis.Tools/ConfigPlugin.cs` — `update_agent_config`

Add `mention_triggers` as a settable field.

### 6. Update docs
**File:** `lis/docs/GROUPS.md` — Mention Detection section

Document the new field and comma-separated format.

## Verification
1. `dotnet build` — no errors
2. `dotnet test` — all pass
3. Commit and push
