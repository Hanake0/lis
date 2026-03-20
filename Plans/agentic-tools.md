# Agentic Coding Tools for Lis

## Context

Lis currently has 5 Semantic Kernel plugins (DateTime, Prompt, Memory, Config, Response) focused on conversation management. It has no tools for interacting with the host system — no shell execution, filesystem access, web fetching, or process management. This limits the AI's ability to assist with coding, system administration, and research tasks.

OpenClaw (TypeScript, same parent folder) has a mature, battle-tested tool system with multi-layer security. We adapt its best patterns to Lis's .NET/Semantic Kernel architecture, keeping the single-operator personal assistant trust model and WhatsApp-as-control-plane UX.

**Key constraint**: Lis communicates exclusively through WhatsApp. All approval flows, notifications, and tool feedback must work within that channel.

---

## Phase 1: Tool Authorization Framework

**Goal**: Gate individual tools by sender identity — the foundation for all security.

### New files

- **[ToolAuthorizationAttribute.cs](Lis.Core/Util/ToolAuthorizationAttribute.cs)** — Mirrors existing `ToolSummarizationAttribute` pattern
  ```csharp
  public enum ToolAuthLevel { Open, OwnerOnly, ApprovalRequired }

  [AttributeUsage(AttributeTargets.Method)]
  public sealed class ToolAuthorizationAttribute(ToolAuthLevel level) : Attribute {
      public ToolAuthLevel Level { get; } = level;
  }
  ```
  - `Open` — Any authorized sender (default, backwards-compatible)
  - `OwnerOnly` — Only `LIS_OWNER_JID` can trigger (ref: OpenClaw's `wrapOwnerOnlyToolExecution()` in `tools/common.ts`)
  - `ApprovalRequired` — Needs explicit approval before execution (ref: OpenClaw's exec approval system)

- **[ToolAuthRegistry.cs](Lis.Agent/ToolAuthRegistry.cs)** — Maps `"{pluginName}-{functionName}"` to `ToolAuthLevel` at registration time. Needed because Semantic Kernel's `KernelFunction.Metadata` doesn't propagate custom attributes. Built once in `AgentSetup`, injected into `ToolRunner`.

### Modified files

- **[ToolContext.cs](Lis.Core/Util/ToolContext.cs)** — Add `SenderJid` (string?) and `IsOwner` (bool) via `AsyncLocal<T>`, matching existing pattern
- **[ConversationService.cs](Lis.Agent/ConversationService.cs):170-174** — Set `ToolContext.SenderJid` and `ToolContext.IsOwner` before the tool loop (alongside existing ChatId/Channel/AgentId assignments)
- **[ToolRunner.cs](Lis.Agent/ToolRunner.cs):112-127** — Add authorization check in `InvokeFunctionAsync` before `call.InvokeAsync()`:
  - Look up auth level from `ToolAuthRegistry`
  - If `OwnerOnly` and `!ToolContext.IsOwner` → return error result to the AI
  - If `ApprovalRequired` → delegate to `IApprovalService` (Phase 2)
- **All existing plugins** — Annotate with `[ToolAuthorization(ToolAuthLevel.Open)]` explicitly

### UX: Zero impact on existing behavior
All current tools get `Open` level — nothing changes for existing users. The attribute is purely additive.

---

## Phase 2: Exec Approval System

**Goal**: Interactive command approval via WhatsApp chat, with persistent allowlists.

Ref: OpenClaw's `bash-tools.exec-approval-request.ts`, `exec-approvals.md`

### New files

- **[ExecApprovalEntity.cs](Lis.Persistence/Entities/ExecApprovalEntity.cs)** — Audit trail + pending state
  ```
  Table: exec_approval
  Columns: id, approval_id (short unique e.g. "a3f7"), chat_id (FK), agent_id (FK),
           command, cwd, status (pending/approved/denied/expired),
           decision (once/always/deny), resolved_by, message_external_id,
           created_at, expires_at, resolved_at
  Indexes: UNIQUE(approval_id), partial on status='pending'
  ```

- **[ExecAllowlistEntity.cs](Lis.Persistence/Entities/ExecAllowlistEntity.cs)** — Persistent command patterns
  ```
  Table: exec_allowlist
  Columns: id, agent_id (FK), pattern (glob), last_used_at, last_command, created_at
  Index: UNIQUE(agent_id, pattern)
  ```

- **[ApprovalService.cs](Lis.Agent/ApprovalService.cs)** — Core approval orchestration
  - `ConcurrentDictionary<string, TaskCompletionSource<ApprovalResult>>` for in-flight approvals
  - `RequestApprovalAsync()`:
    1. Check allowlist first — if command matches a glob pattern, skip approval (ref: OpenClaw allowlist check in `requiresExecApproval`)
    2. Create DB record with short `approval_id`
    3. Send WhatsApp notification to owner chat:
       ```
       🔒 Exec approval required
       ID: a3f7
       Command: git status
       CWD: /workspace/lis
       Expires in: 120s

       Reply: /approve a3f7
       ```
    4. Await `TaskCompletionSource` with timeout
    5. On timeout → deny (ref: OpenClaw `askFallback: "deny"`)
  - `HandleApprovalResponseAsync()` — Called by `/approve` command, completes the TCS
  - Glob matching via `FileSystemName.MatchesSimpleExpression()` (.NET built-in)

- **[ApproveCommand.cs](Lis.Agent/Commands/ApproveCommand.cs)** — `/approve <id> [once|always|deny]`
  - Default action: `once` (fastest UX — just `/approve a3f7` to proceed)
  - `always` → adds pattern to `exec_allowlist` for future bypass
  - `deny` → blocks and returns error to AI
  - Also: `/deny <id>` as shorthand for `/approve <id> deny`

- **Reaction-based approval** — WhatsApp supports reactions on messages. When the approval notification is sent, the owner can react instead of typing:
  - 👍 → `once` (approve this one time)
  - ✅ → `always` (add to allowlist)
  - ❌ → `deny`
  - Implementation details:
    - `IChannelClient.ReactAsync` already exists for outbound reactions
    - GOWA webhook needs a new event handler for incoming reactions (currently only handles `message` and `chat_presence`)
    - Add `IConversationService.HandleReactionAsync(string messageId, string chatId, string emoji, string senderId)`
    - [GowaWebhookController.cs](Lis.Channels/WhatsApp/GowaWebhookController.cs) — detect reaction events from GOWA, extract target message ID + emoji + sender
    - `ApprovalService` maintains a reverse lookup: `message_external_id` → `approval_id`. When a reaction arrives on a tracked message, resolve the matching TCS.
    - Only reactions from the owner JID resolve approvals (security gate)
  - Ref: OpenClaw's `approvals.exec.mode: "session"` allows approving in-chat; reactions are our WhatsApp-native equivalent

### Modified files

- **[LisDbContext.cs](Lis.Persistence/LisDbContext.cs)** — Add `DbSet<ExecApprovalEntity>` and `DbSet<ExecAllowlistEntity>`
- **[AgentSetup.cs](Lis.Agent/AgentSetup.cs)** — Register `IApprovalService`, `ApproveCommand`, `DenyCommand`
- **[ToolRunner.cs](Lis.Agent/ToolRunner.cs)** — Inject `IApprovalService`, wire up `ApprovalRequired` check

### UX considerations
- **Short IDs**: 4-char hex (e.g. `a3f7`) — easy to type on phone
- **Default once**: Just `/approve a3f7` with no second arg, minimal friction
- **React to approve**: Even faster — just react 👍 to the approval message, no typing needed
- **Notification format**: Clean, scannable on WhatsApp's narrow screen
- **Timeout feedback**: AI gets a clear "Approval timed out (120s)" message so it can explain to the user

### Security: Ref OpenClaw exec-approvals.md
- `deny` always wins — if agent's `exec_security = deny`, no amount of approval can override
- Allow-always patterns are per-agent (ref: OpenClaw per-agent allowlists)
- Approval timeout defaults to deny (ref: OpenClaw `askFallback`)
- All approval events logged to DB for audit

---

## Phase 3: Tool Policy Pipeline

**Goal**: Per-agent control over which tools the AI can see and use.

Ref: OpenClaw's `tool-policy-pipeline.ts`, `tool-catalog.ts`

### Schema changes to [AgentEntity](Lis.Persistence/Entities/AgentEntity.cs)

```csharp
[Column("tool_profile")]     public string? ToolProfile { get; set; }     // minimal, standard, coding, full
[Column("tools_allow")]      public string? ToolsAllow { get; set; }      // comma-separated globs: "exec_*,fs_*"
[Column("tools_deny")]       public string? ToolsDeny { get; set; }       // comma-separated globs: "exec_*"
[Column("workspace_path")]   public string? WorkspacePath { get; set; }   // "/workspace/project"
[Column("exec_security")]    public string ExecSecurity { get; set; }     // deny, allowlist, full
[Column("exec_timeout_seconds")] public int ExecTimeoutSeconds { get; set; } // default 120
```

### Tool profiles (ref: OpenClaw `TOOL_PROFILES` in tool-catalog.ts)

| Profile | Tools included |
|---------|---------------|
| `minimal` | dt_*, resp_* |
| `standard` | dt_*, resp_*, mem_*, prompt_*, cfg_*, web_* (default) |
| `coding` | All standard + exec_*, fs_* |
| `full` | Everything (coding + browser_*) |

### Tool groups (ref: OpenClaw `CORE_TOOL_GROUPS`)

| Group | Tools |
|-------|-------|
| `group:runtime` | exec_* |
| `group:fs` | fs_* |
| `group:web` | web_* |
| `group:browser` | browser_* |
| `group:memory` | mem_* |
| `group:config` | cfg_* |

### New file

- **[ToolPolicyService.cs](Lis.Agent/ToolPolicyService.cs)** — Resolves available tools for an agent
  ```csharp
  public IReadOnlyList<KernelFunction> ResolveAvailableTools(Kernel kernel, AgentEntity agent)
  ```
  Pipeline:
  1. Expand profile → base tool set
  2. Apply `tools_allow` globs (if set, only matching pass)
  3. Apply `tools_deny` globs (deny always wins — ref: OpenClaw rule)

### Integration in [ConversationService.cs](Lis.Agent/ConversationService.cs):190-194

```csharp
var availableTools = toolPolicyService.ResolveAvailableTools(kernel, agent);
FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
    functions: availableTools, autoInvoke: false)
```

SK's `FunctionChoiceBehavior.Auto` already accepts a `functions` parameter — clean integration, no hacks.

### UX: AI self-service via ConfigPlugin

Add policy fields to [ConfigPlugin.cs](Lis.Tools/ConfigPlugin.cs):16 `KnownAgentFields`:
```csharp
"tool_profile", "tools_allow", "tools_deny", "workspace_path", "exec_security", "exec_timeout_seconds"
```

This lets the owner say "switch to coding profile" and the AI can `cfg_update_agent_config("tool_profile", "coding")`.

---

## Phase 4: ExecPlugin — Shell Execution

**Goal**: Execute shell commands with workspace sandboxing and approval integration.

Ref: OpenClaw `bash-tools.ts`, `bash-tools.exec-types.ts`

### New file: [ExecPlugin.cs](Lis.Tools/ExecPlugin.cs)

Plugin name: `"exec"` → tools: `exec_run_command`

```csharp
[KernelFunction("run_command")]
[Description("Execute a shell command and return stdout, stderr, and exit code.")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.ApprovalRequired)]
public async Task<string> RunCommandAsync(
    [Description("Shell command to execute")] string command,
    [Description("Working directory (default: workspace root)")] string? cwd = null,
    [Description("Timeout in seconds (default: 30, max: 300)")] int timeoutSeconds = 30)
```

Implementation:
- Uses `System.Diagnostics.Process` with `/bin/bash -c "{command}"` (or `cmd /c` on Windows)
- CWD defaults to agent's `workspace_path`, validated to be within workspace boundaries
- Captures stdout + stderr with configurable size limit (50KB default, truncated with `[...truncated]`)
- Timeout via `CancellationTokenSource` — kills process on timeout
- Returns structured output:
  ```
  Exit: 0
  --- stdout ---
  <output>
  --- stderr ---
  <warnings>
  ```
- Sends tool notification: `🖥️ Running: git status` (via `ToolContext.NotifyAsync`)

### Security (ref: OpenClaw exec security modes)
- **exec_security=deny**: `ToolPolicyService` excludes `exec_*` entirely — AI never sees the tool
- **exec_security=allowlist**: Tool is available, `ApprovalRequired` attribute triggers allowlist check then approval flow
- **exec_security=full**: Tool available, no approval needed (override auth level at runtime)
- Workspace CWD validation: `Path.GetFullPath(cwd, workspacePath)` + `StartsWith` check
- Output size limits prevent context window flooding
- Process timeout prevents resource exhaustion

---

## Phase 5: FileSystemPlugin — File Operations

**Goal**: Read, write, edit, and browse files within a workspace sandbox.

Ref: OpenClaw `pi-tools.read.ts`, `tool-fs-policy.ts`

### New file: [FileSystemPlugin.cs](Lis.Tools/FileSystemPlugin.cs)

Plugin name: `"fs"` → tools: `fs_read_file`, `fs_write_file`, `fs_edit_file`, `fs_list_directory`, `fs_search_files`

```csharp
[KernelFunction("read_file")]
[Description("Read file contents. Returns the content with line numbers.")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
public async Task<string> ReadFileAsync(
    [Description("File path (absolute or relative to workspace)")] string path,
    [Description("Starting line (1-based, default: 1)")] int offset = 1,
    [Description("Max lines to return (default: 200)")] int limit = 200)

[KernelFunction("write_file")]
[Description("Write content to a file, creating directories as needed.")]
[ToolSummarization(SummarizationPolicy.Prune)]
[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
public async Task<string> WriteFileAsync(
    [Description("File path")] string path,
    [Description("Content to write")] string content)

[KernelFunction("edit_file")]
[Description("Find and replace text in a file. old_text must be an exact match.")]
[ToolSummarization(SummarizationPolicy.Prune)]
[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
public async Task<string> EditFileAsync(
    [Description("File path")] string path,
    [Description("Exact text to find")] string oldText,
    [Description("Replacement text")] string newText)

[KernelFunction("list_directory")]
[Description("List files and directories at a path.")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
public async Task<string> ListDirectoryAsync(
    [Description("Directory path")] string path,
    [Description("Include hidden files (default: false)")] bool showHidden = false)

[KernelFunction("search_files")]
[Description("Search for files matching a glob pattern (e.g. '**/*.cs').")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
public async Task<string> SearchFilesAsync(
    [Description("Glob pattern")] string pattern,
    [Description("Search root (default: workspace)")] string? root = null)
```

### Workspace sandbox (ref: OpenClaw `tools.fs.workspaceOnly`)

Shared helper method used by all FS tools:
```csharp
private string ResolveSafePath(string userPath) {
    string workspace = ResolveWorkspacePath();
    string resolved = Path.GetFullPath(userPath, workspace);
    if (!resolved.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
        throw new UnauthorizedAccessException($"Path outside workspace: {userPath}");
    // Also check symlink targets
    if (File.Exists(resolved)) {
        string? linkTarget = File.ResolveLinkTarget(resolved, true)?.FullName;
        if (linkTarget is not null && !linkTarget.StartsWith(workspace))
            throw new UnauthorizedAccessException("Symlink target outside workspace");
    }
    return resolved;
}
```

### UX: Output formatting
- `read_file` returns content with line numbers (`  1 | content...`) for easy reference
- `list_directory` returns tree-like format with file sizes
- `edit_file` confirms the edit with before/after context (3 lines each direction)
- All tools report their workspace-relative path in notifications

---

## Phase 6: WebPlugin — Web Search & Fetch

**Goal**: Give the AI web access for research tasks.

Ref: OpenClaw `web-search.ts`, `web-fetch.ts`

### New file: [WebPlugin.cs](Lis.Tools/WebPlugin.cs)

Plugin name: `"web"` → tools: `web_search`, `web_fetch`

```csharp
[KernelFunction("search")]
[Description("Search the web. Returns titles, URLs, and snippets.")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.Open)]
public async Task<string> SearchAsync(
    [Description("Search query")] string query,
    [Description("Max results (default: 5, max: 10)")] int maxResults = 5)

[KernelFunction("fetch")]
[Description("Fetch a URL and return its text content.")]
[ToolSummarization(SummarizationPolicy.Summarize)]
[ToolAuthorization(ToolAuthLevel.Open)]
public async Task<string> FetchAsync(
    [Description("URL to fetch")] string url,
    [Description("Max content length (default: 10000)")] int maxLength = 10000)
```

### Configuration (env vars in `.env.example`)
```
LIS_WEB_SEARCH_ENABLED=true
LIS_WEB_SEARCH_PROVIDER=brave  # brave, searxng
LIS_WEB_SEARCH_API_KEY=...
LIS_WEB_SEARCH_BASE_URL=...    # for SearXNG self-hosted
```

### Implementation
- Search: HTTP client to Brave Search API or SearXNG instance. Returns structured results.
- Fetch: HTTP GET with timeout (10s), User-Agent header, basic HTML-to-text via regex stripping. Truncates at `maxLength`.
- Auth level `Open` — web search is low-risk, any authorized sender benefits from it.

---

## Phase 7: BrowserPlugin — Chrome Headless Automation

**Goal**: Give the AI a real browser it can control — navigate, click, type, screenshot, extract data.

Ref: OpenClaw's `browser-tool.ts`, `pw-session.ts`, `pw-tools-core.interactions.ts`, `routes/agent.ts`

### Architecture

OpenClaw uses Playwright + CDP with an HTTP server intermediary. For Lis, we use **Microsoft.Playwright** (.NET) directly — no HTTP server needed since we're in-process.

```
BrowserPlugin → BrowserSessionManager → Playwright → Chrome/Chromium (headless)
```

### NuGet dependency

Add to `Lis.Tools.csproj`:
```xml
<PackageReference Include="Microsoft.Playwright" Version="1.*" />
```

Post-build: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` (one-time, add to Dockerfile)

### New files

- **[BrowserPlugin.cs](Lis.Tools/BrowserPlugin.cs)** — Plugin name: `"browser"`

  ```csharp
  [KernelFunction("start")]
  [Description("Launch a headless Chrome browser. Must be called before other browser tools.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> StartAsync(
      [Description("Start URL (default: about:blank)")] string? url = null,
      [Description("Headless mode (default: true)")] bool headless = true)

  [KernelFunction("navigate")]
  [Description("Navigate the browser to a URL.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> NavigateAsync(
      [Description("URL to navigate to")] string url,
      [Description("Wait until: load, domcontentloaded, networkidle (default: load)")] string? waitUntil = null)

  [KernelFunction("snapshot")]
  [Description("Get the current page content as structured text (accessible elements with refs).")]
  [ToolSummarization(SummarizationPolicy.Summarize)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> SnapshotAsync(
      [Description("Max content length (default: 15000)")] int maxLength = 15000)

  [KernelFunction("screenshot")]
  [Description("Take a screenshot of the current page. Returns base64-encoded image.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> ScreenshotAsync(
      [Description("Full page screenshot (default: false)")] bool fullPage = false)

  [KernelFunction("click")]
  [Description("Click an element by CSS selector or text content.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> ClickAsync(
      [Description("CSS selector or text:\"Button Text\"")] string selector)

  [KernelFunction("type")]
  [Description("Type text into a focused element or element matching selector.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> TypeAsync(
      [Description("CSS selector for the input")] string selector,
      [Description("Text to type")] string text)

  [KernelFunction("evaluate")]
  [Description("Execute JavaScript in the browser page context. Returns the result.")]
  [ToolSummarization(SummarizationPolicy.Summarize)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> EvaluateAsync(
      [Description("JavaScript expression to evaluate")] string script)

  [KernelFunction("tabs")]
  [Description("List open browser tabs with their titles and URLs.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> TabsAsync()

  [KernelFunction("close")]
  [Description("Close the browser session.")]
  [ToolSummarization(SummarizationPolicy.Prune)]
  [ToolAuthorization(ToolAuthLevel.OwnerOnly)]
  public async Task<string> CloseAsync()
  ```

- **[BrowserSessionManager.cs](Lis.Tools/Browser/BrowserSessionManager.cs)** — Manages Playwright lifecycle
  - Singleton service (one browser per agent, keyed by `ToolContext.AgentId`)
  - Lazy initialization — browser only starts when `start` tool is called
  - Auto-cleanup on idle timeout (configurable, default 10 min)
  - Methods:
    ```csharp
    Task<IPage> GetOrStartAsync(long agentId, string? url, bool headless, CancellationToken ct)
    Task<IPage?> GetPageAsync(long agentId)
    Task CloseAsync(long agentId)
    ```
  - Ref: OpenClaw's `pw-session.ts` manages Browser/Context/Page lifecycle similarly

### Snapshot format (ref: OpenClaw `pw-tools-core.snapshot.ts`)

The `snapshot` tool returns an AI-friendly text representation of the page DOM, similar to OpenClaw's "ai" format:
```
[page] https://example.com
  [heading] "Welcome to Example"
  [form]
    [textbox name="email"] ""
    [textbox name="password" type="password"] ""
    [button] "Sign In"
  [link] "Forgot password?"
```

This is built using Playwright's `page.Accessibility.SnapshotAsync()` (accessibility tree) which provides a structured view without raw HTML noise.

### Dockerfile addition

```dockerfile
# Install Playwright browsers
RUN dotnet tool install --global Microsoft.Playwright.CLI \
    && playwright install chromium --with-deps
```

### Security
- `OwnerOnly` on all browser tools — web automation is inherently powerful
- Browser runs headless by default — no display required
- Isolated Chromium profile per agent (no access to host browser data)
- JavaScript evaluation is powerful but contained to the browser sandbox
- Consider adding URL allowlist/denylist for `navigate` (nice-to-have)

---

## Registration Summary

In [AgentSetup.cs](Lis.Agent/AgentSetup.cs):

```csharp
// Existing
kernel.Plugins.AddFromType<DateTimePlugin>(pluginName: "dt", serviceProvider: sp);
kernel.Plugins.AddFromType<PromptPlugin>(pluginName: "prompt", serviceProvider: sp);
kernel.Plugins.AddFromType<MemoryPlugin>(pluginName: "mem", serviceProvider: sp);
kernel.Plugins.AddFromType<ConfigPlugin>(pluginName: "cfg", serviceProvider: sp);
kernel.Plugins.AddFromType<ResponsePlugin>(pluginName: "resp", serviceProvider: sp);

// New
kernel.Plugins.AddFromType<ExecPlugin>(pluginName: "exec", serviceProvider: sp);
kernel.Plugins.AddFromType<FileSystemPlugin>(pluginName: "fs", serviceProvider: sp);
kernel.Plugins.AddFromType<WebPlugin>(pluginName: "web", serviceProvider: sp);
kernel.Plugins.AddFromType<BrowserPlugin>(pluginName: "browser", serviceProvider: sp);

// Services
services.AddSingleton<IApprovalService, ApprovalService>();
services.AddSingleton<ToolPolicyService>();
services.AddSingleton<ToolAuthRegistry>();
services.AddSingleton<BrowserSessionManager>();
services.AddSingleton<IChatCommand, ApproveCommand>();
services.AddSingleton<IChatCommand, DenyCommand>();
```

All plugins registered unconditionally — `ToolPolicyService` controls visibility at the `FunctionChoiceBehavior` level.

---

## Database Migration

Single migration covering all schema changes:

```sql
-- Agent policy columns
ALTER TABLE agent ADD COLUMN tool_profile varchar(32);
ALTER TABLE agent ADD COLUMN tools_allow text;
ALTER TABLE agent ADD COLUMN tools_deny text;
ALTER TABLE agent ADD COLUMN workspace_path text;
ALTER TABLE agent ADD COLUMN exec_security varchar(16) NOT NULL DEFAULT 'deny';
ALTER TABLE agent ADD COLUMN exec_timeout_seconds int NOT NULL DEFAULT 120;

-- Exec approval audit trail
CREATE TABLE exec_approval (
    id              bigserial PRIMARY KEY,
    approval_id     varchar(16) NOT NULL UNIQUE,
    chat_id         bigint NOT NULL REFERENCES chat(id),
    agent_id        bigint REFERENCES agent(id),
    command         text NOT NULL,
    cwd             text,
    status          varchar(16) NOT NULL DEFAULT 'pending',
    decision        varchar(16),
    resolved_by     varchar(64),
    message_external_id varchar(128),   -- WhatsApp msg ID for reaction-based approval
    created_at      timestamptz NOT NULL,
    expires_at      timestamptz NOT NULL,
    resolved_at     timestamptz
);

-- Exec allowlist (per-agent glob patterns)
CREATE TABLE exec_allowlist (
    id              bigserial PRIMARY KEY,
    agent_id        bigint REFERENCES agent(id),
    pattern         text NOT NULL,
    last_used_at    timestamptz,
    last_command    text,
    created_at      timestamptz NOT NULL,
    UNIQUE(agent_id, pattern)
);
```

---

## Security Model Summary

### Trust layers (ref: OpenClaw SECURITY.md)

```
Sender → ShouldRespond gate → Tool Policy (profile/allow/deny) → Tool Auth (owner/approval) → Execution
```

| Layer | What it controls | Where enforced |
|-------|-----------------|----------------|
| **ShouldRespond** | Can this sender talk to the AI at all? | `AgentService.ShouldRespond` |
| **Tool Policy** | Which tools does the AI see? | `ToolPolicyService` → `FunctionChoiceBehavior` |
| **Tool Auth** | Who can trigger this specific tool? | `ToolRunner.InvokeFunctionAsync` via `ToolAuthRegistry` |
| **Exec Approval** | Is this specific command allowed? | `ApprovalService` (allowlist + interactive) |
| **Workspace Sandbox** | Can this path be accessed? | `FileSystemPlugin.ResolveSafePath` / `ExecPlugin` CWD check |

### Defaults (secure by default)
- New agents get `tool_profile = "standard"` (includes web search, no exec/fs/browser)
- `exec_security = "deny"` by default
- All dangerous tools are `OwnerOnly` or `ApprovalRequired`
- Workspace sandbox prevents path traversal
- Approval timeout defaults to deny
- Reactions on approval messages resolve instantly (👍=once, ✅=always, ❌=deny)

---

## Nice-to-Haves (Post-MVP)

1. **Allowlist management tools** — `exec_list_allowlist`, `exec_add_allowlist`, `exec_remove_allowlist` as AI tools for self-service
2. **Background process support** — Long-running processes with `exec_process_start`, `exec_process_status`, `exec_process_kill` (ref: OpenClaw `bash-tools.process.ts`)
3. **Safe bins** — Always-allowed stdin-only binaries like `jq`, `head`, `tail` that skip approval even in allowlist mode (ref: OpenClaw `tools.exec.safeBins`)
4. **File content search (grep)** — `fs_grep` tool for searching file contents with regex
5. **Docker sandboxing** — Spawn isolated containers for exec (ref: OpenClaw `sandbox/docker.ts`)
6. **Chat-level tool policy** — Per-chat tool overrides on top of per-agent
7. **Tool usage analytics** — Track tool call frequency/duration per agent for observability
8. **Cron/scheduled tasks** — `cron_create`, `cron_list`, `cron_delete` (ref: OpenClaw `group:automation`)
9. **Git plugin** — Dedicated `git_status`, `git_diff`, `git_commit` tools with safer UX than raw exec
10. **Browser URL allowlist/denylist** — Restrict which domains the browser can navigate to (per-agent config)
11. **Browser cookie/storage persistence** — Save/load browser sessions across restarts for authenticated workflows
12. **Browser download handling** — Download files and make them accessible to other tools (ref: OpenClaw `routes/agent.ts` download endpoints)

---

## Verification Plan

### Unit tests
- `ToolAuthRegistry` correctly maps plugins to auth levels
- `ToolPolicyService` correctly filters tools by profile/allow/deny/groups
- `ApprovalService` approval/deny/timeout flows
- `ApprovalService` reaction-based resolution (👍/✅/❌)
- `FileSystemPlugin.ResolveSafePath` rejects traversal, symlinks
- `ExecPlugin` respects workspace CWD, timeout, output limits
- `BrowserSessionManager` lifecycle (start, page ops, idle cleanup)

### Integration tests
- Full approval flow: AI calls exec → approval message sent → `/approve` command → tool executes
- Reaction approval flow: AI calls exec → approval sent → owner reacts 👍 → tool executes
- Owner-only gate: non-owner conversation triggers OwnerOnly tool → gets error
- Tool policy: agent with `coding` profile sees exec tools; agent with `standard` sees web but not exec
- Browser: start → navigate → snapshot → close lifecycle

### Manual E2E
- Send message asking AI to run `git status` → approval prompt arrives → react 👍 → result returned
- Change agent to `coding` profile via `cfg_update_agent_config`
- Verify filesystem sandbox blocks `../../etc/passwd`
- Verify approval timeout returns deny after configured seconds
- Ask AI to open a website in browser, take a screenshot, extract data

---

## Documentation

Write docs at `lis/docs/` for each major system:

- **`docs/TOOLS.md`** — Overview of all tool plugins, their functions, auth levels, and profiles
- **`docs/TOOL_POLICY.md`** — How tool profiles, allow/deny, and groups work. How to configure per-agent.
- **`docs/EXEC_APPROVALS.md`** — Approval flow, allowlists, reaction-based approval, security modes
- **`docs/BROWSER.md`** — Browser plugin setup, Playwright install, Dockerfile changes, snapshot format
- **`docs/SECURITY_MODEL.md`** — Trust layers, threat model, defaults, operational guidance

---

## Plan Archive

Copy this plan to `lis/Plans/agentic-tools.md` for permanent reference alongside existing plans ([multi-agent.md](lis/Plans/multi-agent.md), [response-control.md](lis/Plans/response-control.md), etc.)

---

## Commit Strategy

Follow project convention: gitmoji + conventional commits, micro-commits per logical unit.

Each phase gets multiple commits, one per logical task:

**Phase 1** (3 commits):
1. `✨ feat(core): add ToolAuthorizationAttribute and ToolAuthLevel enum`
2. `✨ feat(agent): add ToolAuthRegistry and sender context to ToolContext`
3. `✨ feat(agent): integrate tool authorization into ToolRunner`

**Phase 2** (4 commits):
1. `✨ feat(persistence): add ExecApproval and ExecAllowlist entities + migration`
2. `✨ feat(agent): add ApprovalService with allowlist matching and TCS flow`
3. `✨ feat(agent): add /approve and /deny commands`
4. `✨ feat(channels): add reaction-based approval handling to webhook`

**Phase 3** (2 commits):
1. `✨ feat(persistence): add tool policy columns to AgentEntity + migration`
2. `✨ feat(agent): add ToolPolicyService and integrate with ConversationService`

**Phase 4** (1 commit):
1. `✨ feat(tools): add ExecPlugin with shell execution and workspace sandboxing`

**Phase 5** (1 commit):
1. `✨ feat(tools): add FileSystemPlugin with workspace sandbox`

**Phase 6** (1 commit):
1. `✨ feat(tools): add WebPlugin with search and fetch`

**Phase 7** (2 commits):
1. `✨ feat(tools): add BrowserSessionManager with Playwright lifecycle`
2. `✨ feat(tools): add BrowserPlugin with headless Chrome automation`

**Docs** (1 commit):
1. `📝 docs: add tool system documentation and archive plan`

Co-author on all: `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>`

---

## Implementation Strategy

Each phase must be fully completed, built, tested, and committed before moving to the next. No skipping or deferring within a phase.

**Per-commit checklist:**
1. Write/modify all files for the logical task
2. `dotnet build` — must pass with zero errors
3. `dotnet test` — must pass
4. `jb cleanupcode` — format per project rules
5. `git add` + `git commit` with correct gitmoji message
6. Verify the commit is clean before proceeding

**Subagent strategy:**
- Use subagents for implementation when the task is self-contained and has clear boundaries
- Pass full context to subagents: file paths, existing patterns, code snippets from the plan, DB schema, attribute conventions
- Subagents should NOT skip steps — they must build, test, and verify
- Only parallelize subagents when tasks are truly independent (e.g., two plugins with no shared dependencies)
