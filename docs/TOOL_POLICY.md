# Tool Policy

Tool policy controls which tools the AI can see and use for a given agent. It is enforced by `ToolPolicyService` in `Lis.Agent`, which filters the kernel's registered functions before each conversation turn.

## Profiles

Each agent has a `tool_profile` setting (default: `standard`). The profile determines which plugin prefixes are included:

| Profile | Included Prefixes | Use Case |
|---------|-------------------|----------|
| `minimal` | `dt_*`, `resp_*` | Read-only, no persistent actions |
| `standard` | `dt_*`, `resp_*`, `mem_*`, `prompt_*`, `cfg_*`, `web_*` | General assistant with memory and config |
| `coding` | standard + `exec_*`, `fs_*` | Development workflows with shell and filesystem |
| `full` | everything (no prefix filter) | All tools including `browser_*` |

The `full` profile uses an empty prefix array, which means all registered functions pass the profile filter.

## Allow / Deny Globs

After the profile filter, two additional filters apply:

- **`tools_allow`** — comma-separated glob patterns. If set, only matching tools pass.
- **`tools_deny`** — comma-separated glob patterns. Matching tools are excluded. **Deny always wins over allow.**

Globs use .NET's `FileSystemName.MatchesSimpleExpression` (supports `*` and `?`).

### Examples

```
tools_allow = exec_*, fs_read_file
tools_deny  = cfg_update_*
```

This allows exec tools and `fs_read_file`, but blocks all `cfg_update_*` functions even if they pass the profile and allow filters.

## Tool Groups

For convenience, group shorthands expand to plugin prefixes in allow/deny patterns:

| Group | Expands To |
|-------|------------|
| `group:runtime` | `exec_*` |
| `group:fs` | `fs_*` |
| `group:web` | `web_*` |
| `group:browser` | `browser_*` |
| `group:memory` | `mem_*` |
| `group:config` | `cfg_*` |

Groups can be used anywhere a glob is accepted:

```
tools_deny = group:browser, group:runtime
```

## Exec Security Override

The `exec_security` agent setting provides an additional gate specifically for exec tools:

| Value | Behavior |
|-------|----------|
| `deny` | Exec tools are hidden from the AI entirely (filtered out regardless of profile) |
| `allowlist` | Exec tools are visible but require approval; allowlist matches bypass approval |
| `full` | Exec tools run without approval |

See [EXEC_APPROVALS](EXEC_APPROVALS.md) for the full approval flow.

## Resolution Order

`ToolPolicyService.ResolveAvailableTools` applies filters in this order:

1. **Profile filter** — tool name must match a prefix in the active profile
2. **Allow filter** — if `tools_allow` is set, tool must match at least one pattern
3. **Deny filter** — if `tools_deny` is set, tool must NOT match any pattern
4. **Exec security** — if `exec_security == "deny"`, all `exec_*` tools are excluded

A tool must pass all four gates to be available to the AI.

## Configuration

All settings are per-agent and can be changed via `cfg_update_agent_config`:

```
cfg_update_agent_config("tool_profile", "coding")
cfg_update_agent_config("tools_deny", "group:browser")
cfg_update_agent_config("exec_security", "allowlist")
```

Changes take effect on the next conversation turn.
