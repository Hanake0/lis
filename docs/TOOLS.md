# Tool Plugins

Lis exposes AI capabilities through Semantic Kernel plugins. Each plugin registers one or more `[KernelFunction]` methods that the AI can call during conversation. Functions are namespaced as `{pluginName}_{functionName}` (e.g. `exec_run_command`).

## Authorization Levels

Every function carries a `[ToolAuthorization]` attribute:

| Level | Behavior |
|-------|----------|
| `Open` | Any sender can trigger |
| `OwnerOnly` | Only the owner JID (checked via `ToolContext.IsOwner`) |
| `ApprovalRequired` | Requires explicit approval before execution (see [EXEC_APPROVALS](EXEC_APPROVALS.md)) |

Authorization is enforced at runtime by `ToolRunner` using the `ToolAuthRegistry`, which builds a map of function-to-level at startup via reflection.

## Summarization Policies

Each function also has a `[ToolSummarization]` attribute controlling how its result is handled during context compaction:

| Policy | Behavior |
|--------|----------|
| `Prune` | Result removed entirely when outside the keep window |
| `Summarize` | Result replaced with an LLM-generated summary |

## Plugin Reference

### DateTimePlugin (`dt`)

| Function | Description | Auth |
|----------|-------------|------|
| `get_current_datetime` | Returns current date/time in Brasilia timezone (BRT), formatted in pt-BR | Open |

### PromptPlugin (`prompt`)

| Function | Description | Auth |
|----------|-------------|------|
| `list_prompt_sections` | List prompt sections (names-only or full content) | Open |
| `get_prompt_section` | Get content of a specific section by name | Open |
| `update_prompt_section` | Update section content (takes effect next message) | Open |

### MemoryPlugin (`mem`)

| Function | Description | Auth |
|----------|-------------|------|
| `create_memory` | Store a new memory, optionally linked to a contact | Open |
| `search_memories` | Search by keyword/phrase with vector or FTS fallback | Open |
| `update_memory` | Update existing memory content by ID | Open |
| `delete_memory` | Delete a memory by ID | Open |

Supports pgvector cosine-distance search when an `IEmbeddingGenerator` is registered; falls back to PostgreSQL full-text search otherwise.

### ConfigPlugin (`cfg`)

| Function | Description | Auth |
|----------|-------------|------|
| `get_agent_config` | Read all agent configuration fields | Open |
| `update_agent_config` | Update a single agent config key | OwnerOnly |
| `get_chat_config` | Read current chat configuration | Open |
| `update_chat_config` | Update a single chat config key | OwnerOnly |
| `add_allowed_sender` | Add a sender to the chat's allowed list | OwnerOnly |
| `remove_allowed_sender` | Remove a sender from the allowed list | OwnerOnly |
| `list_allowed_senders` | List all allowed senders for the chat | Open |
| `list_chats` | List all chats with their configuration | OwnerOnly |
| `manage_chat` | Update config on any chat by external ID | OwnerOnly |

Agent config keys: `model`, `max_tokens`, `context_budget`, `thinking_effort`, `tool_notifications`, `compaction_threshold`, `keep_recent_tokens`, `tool_prune_threshold`, `tool_keep_threshold`, `tool_summarization_policy`, `display_name`, `group_context_prompt`, `tool_profile`, `tools_allow`, `tools_deny`, `workspace_path`, `exec_security`, `exec_timeout_seconds`.

### ResponsePlugin (`resp`)

| Function | Description | Auth |
|----------|-------------|------|
| `react_to_message` | React to a message with an emoji (by message ID or latest) | Open |

### ExecPlugin (`exec`)

| Function | Description | Auth |
|----------|-------------|------|
| `run_command` | Execute a shell command and return stdout/stderr/exit code | ApprovalRequired |

- Runs `cmd /c` on Windows, `/bin/bash -c` on Linux
- Working directory defaults to agent's `workspace_path`; `cwd` parameter must stay within workspace
- Output capped at 50 KB per stream; timeout configurable (default 30s, max 300s)
- Process tree killed on timeout

### FileSystemPlugin (`fs`)

| Function | Description | Auth |
|----------|-------------|------|
| `read_file` | Read file with line numbers (offset + limit pagination) | OwnerOnly |
| `write_file` | Write content to file, creating parent dirs as needed | OwnerOnly |
| `edit_file` | Find-and-replace first occurrence of exact text in a file | OwnerOnly |
| `list_directory` | List dirs (trailing `/`) then files with sizes | OwnerOnly |
| `search_files` | Recursive glob search within workspace (max 100 results) | OwnerOnly |

All paths are resolved relative to the agent's workspace and sandboxed: symlinks pointing outside the workspace are rejected.

### WebPlugin (`web`)

| Function | Description | Auth |
|----------|-------------|------|
| `search` | Web search via Brave Search API (1-10 results) | Open |
| `fetch` | Fetch a URL, strip HTML tags, return plain text (max 50 KB) | Open |

Requires `LIS_WEB_SEARCH_ENABLED=true` and `LIS_WEB_SEARCH_API_KEY` for search.

### BrowserPlugin (`browser`)

| Function | Description | Auth |
|----------|-------------|------|
| `start` | Launch headless Chromium, optionally navigate to URL | OwnerOnly |
| `navigate` | Navigate to URL with configurable wait condition | OwnerOnly |
| `snapshot` | Get page content as plain text (body innerText) | OwnerOnly |
| `screenshot` | Take PNG screenshot, returned as base64 data URI | OwnerOnly |
| `click` | Click element by CSS selector | OwnerOnly |
| `type` | Fill form field by CSS selector | OwnerOnly |
| `evaluate` | Execute JavaScript and return JSON result | OwnerOnly |
| `tabs` | List all open tabs with titles and URLs | OwnerOnly |
| `close` | Close browser and end session | OwnerOnly |

Sessions are managed per-agent by `BrowserSessionManager` (singleton). Only available in the `full` tool profile. See [BROWSER](BROWSER.md) for setup.

## Tool Notifications

When `tool_notifications` is enabled on the agent, tools call `ToolContext.NotifyAsync()` to send a status message to the chat before executing (e.g. "Reading file...", "Searching..."). This gives the user real-time visibility into what the AI is doing.
