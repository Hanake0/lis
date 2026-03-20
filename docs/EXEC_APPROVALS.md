# Exec Approvals

When `exec_security` is set to `allowlist` (the default for agents with exec tools), every `exec_run_command` call goes through the approval flow before execution.

## Flow

```
AI calls exec_run_command("git status")
        │
        ▼
ToolRunner detects ApprovalRequired auth level
        │
        ▼
ApprovalService.RequestApprovalAsync()
        │
        ├─── Check allowlist → match found → execute immediately
        │
        └─── No match → send notification → wait for resolution
                │
                ├── /approve <id>         → Once (execute)
                ├── /approve <id> always  → Always (execute + add to allowlist)
                ├── /approve <id> deny    → Deny (reject)
                ├── /deny <id>            → Deny (reject)
                ├── 👍 reaction           → Once
                ├── ✅ reaction           → Always (+ add to allowlist)
                ├── ❌ reaction           → Deny
                └── timeout               → Deny (default)
```

## Notification Format

When approval is needed, the bot sends a message to the chat:

```
🔒 Exec approval required
ID: a1b2
Command: git status
Expires in: 120s

Reply: /approve a1b2
Or react: 👍 once | ✅ always | ❌ deny
```

The approval ID is a 4-character hex string generated from `RandomNumberGenerator`.

## Resolution Methods

### Command-based

| Command | Effect |
|---------|--------|
| `/approve <id>` | Approve once (default) |
| `/approve <id> once` | Approve once |
| `/approve <id> always` | Approve once and add command to allowlist |
| `/approve <id> deny` | Deny execution |
| `/deny <id>` | Deny execution |

### Reaction-based

React to the approval notification message with:

| Emoji | Effect |
|-------|--------|
| 👍 | Approve once |
| ✅ | Approve once and add to allowlist |
| ❌ | Deny execution |

Only the owner JID can resolve approvals (checked in `ConversationService.HandleReactionAsync`). Reactions from other senders are ignored.

## Allowlist

The `exec_allowlist` table stores glob patterns that bypass the approval flow:

| Column | Description |
|--------|-------------|
| `agent_id` | Scoped to agent (NULL = global) |
| `pattern` | Glob pattern matched via `FileSystemName.MatchesSimpleExpression` |
| `last_used_at` | Updated each time the pattern matches |
| `last_command` | The actual command that last matched |

When a user approves with "always" (via `/approve <id> always` or the ✅ reaction), the exact command string is added as a literal pattern to the allowlist.

Allowlist matching checks both agent-scoped and global (agent_id = NULL) entries.

## Security Modes

Per-agent `exec_security` setting:

| Mode | Visibility | Approval | Allowlist |
|------|------------|----------|-----------|
| `deny` | Exec tools hidden from AI | N/A | N/A |
| `allowlist` | Exec tools visible | Required (unless allowlisted) | Active |
| `full` | Exec tools visible | Not required | N/A |

## Timeout

Default approval timeout is 120 seconds (configured in `ApprovalRequest`). The per-agent `exec_timeout_seconds` config controls the command execution timeout (default 30s, max 300s) -- this is separate from the approval timeout.

If no resolution arrives before the timeout, the approval defaults to deny.

## Persistence

Approval requests are persisted to the `exec_approval` table with status tracking:

| Status | Meaning |
|--------|---------|
| `pending` | Awaiting resolution |
| `approved` | Approved (once or always) |
| `denied` | Explicitly denied |
| `expired` | Timed out without resolution |

The `message_external_id` column links the approval notification to the WhatsApp message, enabling reaction-based resolution via `ResolveByMessageAsync`.
