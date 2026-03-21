# Security Model

Lis uses a 5-layer trust model. Each layer is independent — a request must pass all applicable layers to succeed.

## Trust Layers

```
Incoming message
    │
    ▼
┌─────────────────────────┐
│ 1. ShouldRespond        │  Can this sender talk to the AI?
└─────────────────────────┘
    │
    ▼
┌─────────────────────────┐
│ 2. Tool Policy           │  Which tools does the AI see?
└─────────────────────────┘
    │
    ▼
┌─────────────────────────┐
│ 3. Tool Auth             │  Is this sender allowed to trigger this tool?
└─────────────────────────┘
    │
    ▼
┌─────────────────────────┐
│ 4. Exec Approval         │  Is this specific command allowed?
└─────────────────────────┘
    │
    ▼
┌─────────────────────────┐
│ 5. Workspace Sandbox     │  Can this path/command be accessed?
└─────────────────────────┘
```

## Layer 1: ShouldRespond

Determines whether the AI responds to a message at all. Evaluated in `AgentService.ShouldRespond`.

| Check | Rule |
|-------|------|
| Chat enabled | `chat.Enabled` must be true |
| Owner bypass | Owner JID always passes |
| Sender authorized | Sender must be in `AllowedSenders` list, OR the chat is a group with `OpenGroup = true` |
| Mention gate | In groups with `RequireMention = true`, the bot must be mentioned |

The owner JID (from `LIS_OWNER_JID` env var) bypasses all sender checks.

## Layer 2: Tool Policy

Determines which tools are visible to the AI for a given agent. Evaluated by `ToolPolicyService`.

- **Profile** — base set of tools (`minimal`, `standard`, `coding`, `full`)
- **Allow globs** — if set, further restricts to matching tools
- **Deny globs** — excludes matching tools (deny wins)
- **Exec security** — `deny` mode hides all exec tools

See [TOOL_POLICY](TOOL_POLICY.md) for full details.

## Layer 3: Tool Auth

Runtime authorization check when the AI attempts to call a tool. Enforced by `ToolRunner`.

| Level | Behavior |
|-------|----------|
| `Open` | Always allowed |
| `OwnerOnly` | `ToolContext.IsOwner` must be true; non-owner calls return an error message |
| `ApprovalRequired` | Passes to Layer 4 |

The `ToolAuthRegistry` maps each kernel function to its auth level at startup by reading `[ToolAuthorization]` attributes via reflection.

**Important**: Tool auth is checked at invocation time, not at tool listing time. A non-owner can see OwnerOnly tools in the AI's function list (if the profile allows it), but the AI will get an error if it tries to call them on behalf of a non-owner.

## Layer 4: Exec Approval

Only applies to `ApprovalRequired` tools (currently just `exec_run_command`).

| Security Mode | Behavior |
|---------------|----------|
| `deny` | Tool never reaches this layer (hidden in Layer 2) |
| `allowlist` | Checks allowlist first; if no match, sends approval notification and waits |
| `full` | Skips approval entirely |

See [EXEC_APPROVALS](EXEC_APPROVALS.md) for the full flow.

## Layer 5: Workspace Sandbox

File and command operations are restricted to the agent's workspace directory.

### FileSystemPlugin

- All paths resolved via `Path.GetFullPath(userPath, workspace)`
- Path must start with the workspace prefix (case-insensitive)
- Symlinks are followed and the resolved target is also checked against the workspace boundary
- Violations throw `UnauthorizedAccessException`

### ExecPlugin

- Working directory defaults to `agent.WorkspacePath`
- The `cwd` parameter is resolved relative to the workspace
- `cwd` must remain within the workspace boundary
- The command itself is NOT sandboxed (it can access any path the OS user can) -- this is why exec tools require approval

## Defaults

| Setting | Default |
|---------|---------|
| `tool_profile` | `standard` (no exec, fs, or browser) |
| `exec_security` | `deny` (exec tools hidden) |
| `chat.Enabled` | true (groups/owner DMs), false (non-owner DMs) |
| `chat.OpenGroup` | false (explicit sender allowlist required) |
| `chat.RequireMention` | true (groups), false (DMs) |

A freshly created agent with default settings has no access to exec, filesystem, or browser tools. Enabling them requires explicit configuration.

New non-owner DMs are disabled by default — the owner must enable them via `manage_chat` or natural language from their own chat.

## Threat Model

### Prompt injection

An attacker sends a message that tricks the AI into calling dangerous tools.

**Mitigations**: OwnerOnly auth on destructive tools (fs, browser), approval flow on exec, workspace sandboxing, deny-by-default tool profiles.

### Unauthorized sender

A non-owner sends messages to a chat the AI monitors.

**Mitigations**: ShouldRespond gate, AllowedSenders list, OwnerOnly tool auth (tool calls from non-owner conversations are rejected), non-owner DMs disabled by default, config write tools restricted to owner, slash commands for agent/model management restricted to owner.

### Workspace escape

A malicious path or symlink attempts to read/write outside the workspace.

**Mitigations**: Path prefix validation, symlink target resolution check, `UnauthorizedAccessException` on violations.

### Command injection

A crafted command string exploits shell interpretation.

**Mitigations**: Approval flow requires explicit human review of each command. Allowlist patterns can be narrowly scoped.

## Operational Guidance

1. **Start with `standard` profile** — only enable exec/fs when the agent needs them
2. **Use `allowlist` exec security** — never set `full` unless the agent operates in a fully trusted environment
3. **Scope workspaces narrowly** — set `workspace_path` to the specific project directory, not a home directory
4. **Use deny globs to restrict within a profile** — e.g. `tools_deny = cfg_update_agent_config` to prevent the AI from changing its own config
5. **Review allowlist entries** — commands approved with "always" become permanent; audit the `exec_allowlist` table periodically
6. **Keep groups locked down** — use `OpenGroup = false` and explicit `AllowedSenders` for group chats
7. **Use `list_chats` and `manage_chat`** to remotely manage chat access from the owner's conversation
