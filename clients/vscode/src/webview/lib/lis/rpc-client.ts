/**
 * Webview-side RPC client for the Lis backend.
 *
 * Composes {@link LisHttpClient} (REST via direct `fetch`) and
 * {@link LisEventChannel} (SSE via native `EventSource`). The webview's
 * `connect-src` CSP must include the configured `baseOrigin` — the providers
 * splice that in at HTML render time.
 *
 * Config (origin + token) arrives from the extension host via the initial
 * `init` postMessage and is refreshed via `config_update`.
 *
 * Method signatures intentionally mirror the slice of OC's `OpencodeService`
 * surface we actually use, so existing OC stores can drop in unchanged.
 */

import { LisHttpClient, type HttpRequest } from './http-client';
import { LisEventChannel, type ChannelEventListener, type ChannelStateListener, type EventSourceFactory } from './event-channel';
import { LisChatStreamChannel } from './chat-stream-channel';
import { lisLog, setLisLogVerbose } from './logger';

type HttpMethod = HttpRequest['method'];

interface VsCodeApi {
	postMessage(msg: unknown): void;
}

/** Minimal subset of EventTarget that LisRpcClient needs. In the webview this is `window`. */
export interface MessageSource {
	addEventListener(type: 'message', handler: (e: { data: unknown }) => void): void;
	removeEventListener?(type: 'message', handler: (e: { data: unknown }) => void): void;
}

interface InitMessage {
	type:       'lis.init';
	baseOrigin: string;
	token:      string;
	verbose?:   boolean;
}

interface ConfigUpdateMessage {
	type:       'lis.config_update';
	baseOrigin: string;
	token:      string;
	verbose?:   boolean;
}

export class LisRpcError extends Error {
	constructor(public readonly status: number, public readonly body: unknown) {
		super(typeof body === 'object' && body !== null && 'error' in body
			? String((body as { error: unknown }).error)
			: `request failed (${status})`);
		this.name = 'LisRpcError';
	}
}

export class LisRpcClient {
	private static singleton: LisRpcClient | null = null;
	private readonly http:    LisHttpClient;
	private readonly channel: LisEventChannel;
	/** Stored so per-chat SSE channels use the same factory the test harness injected. */
	private readonly eventSourceFactory?: EventSourceFactory;
	/** Fires every time `setConfig` lands a non-empty origin. Lets stores re-bootstrap
	 *  data when the host's `lis.init` arrives after their first render. */
	private readonly configureListeners = new Set<() => void>();
	/** Resolves on the first non-empty {@link setConfig}. Callers `await rpc.ready()`
	 *  before mounting UI so stores never run on an unconfigured client. */
	private readyResolve: (() => void) | null = null;
	private readonly readyPromise: Promise<void> = new Promise((resolve) => { this.readyResolve = resolve; });

	private constructor(source: MessageSource, eventSourceFactory?: EventSourceFactory) {
		this.http               = new LisHttpClient();
		this.channel            = new LisEventChannel('', '', eventSourceFactory);
		this.eventSourceFactory = eventSourceFactory;

		source.addEventListener('message', (e) => this.onHostMessage(e.data));
	}

	/** Wire the singleton once near webview boot. Subsequent calls return the existing instance.
	 *  Pass `vscode` only if you also want the client to signal readiness back to the host
	 *  (e.g. for a "request initial config" handshake). */
	static install(source?: MessageSource, opts?: { vscode?: VsCodeApi; eventSourceFactory?: EventSourceFactory }): LisRpcClient {
		const target = source ?? (typeof window !== 'undefined' ? window : undefined);
		if (!target) throw new Error('LisRpcClient.install: no MessageSource available (pass one explicitly in non-browser environments)');
		LisRpcClient.singleton ??= new LisRpcClient(target, opts?.eventSourceFactory);
		// Ask the host for current config — the host responds with `lis.init`. Safe to spam:
		// the host just re-sends the same init.
		if (opts?.vscode) opts.vscode.postMessage({ type: 'lis.ready' });
		return LisRpcClient.singleton;
	}

	static instance(): LisRpcClient {
		if (!LisRpcClient.singleton) throw new Error('LisRpcClient.install() must be called before use');
		return LisRpcClient.singleton;
	}

	/** Subscribe to all SSE events. Returns an unsubscribe function. */
	onSse(fn: ChannelEventListener): () => void { return this.channel.onEvent(fn); }

	/** Subscribe to connection-state transitions on the global event stream. */
	onState(fn: ChannelStateListener): () => void { return this.channel.onState(fn); }

	/**
	 * Open a per-chat SSE token stream against `/api/chats/{id}/stream`.
	 *
	 * The returned channel reads its own base origin / token from this RPC
	 * client's current config — call `open()` on the returned channel and
	 * subscribe via `onEvent`. The caller owns its lifecycle (close on chatId
	 * change, on unmount, etc.).
	 */
	openChatStream(chatId: string): LisChatStreamChannel {
		return new LisChatStreamChannel(
			this.http.configuredOrigin,
			this.http.configuredToken,
			chatId,
			this.eventSourceFactory,
		);
	}

	/** Internals — exposed for tests; production code goes through `install` + postMessage. */
	setConfig(baseOrigin: string, token: string): void {
		this.http.updateConfig(baseOrigin, token);
		this.channel.updateConfig(baseOrigin, token);
		// Open (or reopen if config changed) the SSE channel.
		if (baseOrigin) this.channel.open();
		else            this.channel.close();
		// Notify configure subscribers — only when there's actually an origin to talk to,
		// otherwise consumers retry against an empty client and burn a re-render for nothing.
		if (baseOrigin) {
			for (const fn of this.configureListeners) {
				try { fn(); } catch { /* swallow */ }
			}
			// First non-empty config unblocks anyone awaiting `ready()`. Subsequent
			// resolves are no-ops — the underlying Promise is already settled.
			this.readyResolve?.();
			this.readyResolve = null;
		}
	}

	/**
	 * Promise that resolves the first time the host pushes a non-empty config via
	 * `lis.init` / `lis.config_update`. Webview entry points `await rpc.ready()`
	 * before mounting React so no store ever runs against an unconfigured client.
	 * Already-configured: resolves immediately.
	 */
	ready(): Promise<void> { return this.readyPromise; }

	/**
	 * Subscribe to "config became available" — fires every time {@link setConfig}
	 * is called with a non-empty origin (initial bootstrap and subsequent
	 * `lis.config_update` events). Used by stores that need to retry their
	 * initial REST bootstrap if their first render lost the race against
	 * `lis.init` from the extension host. Returns an unsubscribe function.
	 */
	onConfigure(fn: () => void): () => void {
		this.configureListeners.add(fn);
		// Fire immediately if config is already in place — saves the caller from a
		// branch like "if (currentOrigin) doIt() else onConfigure(doIt)".
		if (this.http.configuredOrigin) {
			try { fn(); } catch { /* swallow */ }
		}
		return () => this.configureListeners.delete(fn);
	}

	// ── Generic helpers ────────────────────────────────────────────

	get<T>   (path: string, query?: HttpRequest['query']): Promise<T> { return this.request<T>('GET',    path, undefined, query); }
	delete<T>(path: string, query?: HttpRequest['query']): Promise<T> { return this.request<T>('DELETE', path, undefined, query); }
	post<T>  (path: string, body?: unknown):               Promise<T> { return this.request<T>('POST',   path, body); }
	put<T>   (path: string, body?: unknown):               Promise<T> { return this.request<T>('PUT',    path, body); }
	patch<T> (path: string, body?: unknown):               Promise<T> { return this.request<T>('PATCH',  path, body); }

	// ── Projects ───────────────────────────────────────────────────

	listProjects():                                                   Promise<unknown[]> { return this.get('/projects'); }
	getProject(id: string):                                           Promise<unknown>   { return this.get(`/projects/${encodeURIComponent(id)}`); }
	createProject(input: { path: string; label?: string; color?: string }): Promise<unknown> { return this.post('/projects', input); }
	updateProject(id: string, input: Record<string, unknown>):        Promise<unknown>   { return this.patch(`/projects/${encodeURIComponent(id)}`, input); }
	deleteProject(id: string):                                        Promise<void>      { return this.delete(`/projects/${encodeURIComponent(id)}`); }
	reorderProjects(ids: string[]):                                   Promise<void>      { return this.post('/projects/reorder', { ordered_external_ids: ids }); }
	touchProject(id: string):                                         Promise<unknown>   { return this.post(`/projects/${encodeURIComponent(id)}/touch`); }
	setProjectIcon(id: string, input: { mime: string; blob_base64: string; source: 'custom' | 'auto' }): Promise<unknown> {
		return this.post(`/projects/${encodeURIComponent(id)}/icon`, input);
	}
	removeProjectIcon(id: string):                                    Promise<unknown>   { return this.delete(`/projects/${encodeURIComponent(id)}/icon`); }
	moveChat(chatId: string, projectId: string | null):               Promise<void>      { return this.post(`/chats/${encodeURIComponent(chatId)}/move`, { project_external_id: projectId }); }

	// ── Project metadata (per-project KV: notes, todos, setup commands, plan file) ──

	listProjectMetadata(projectId: string): Promise<{ key: string; value: string; updated_at: string }[]> {
		return this.get(`/projects/${encodeURIComponent(projectId)}/metadata`);
	}
	getProjectMetadata(projectId: string, key: string): Promise<{ key: string; value: string | null; found: boolean; updated_at: string | null }> {
		return this.get(`/projects/${encodeURIComponent(projectId)}/metadata/${encodeURIComponent(key)}`);
	}
	setProjectMetadata(projectId: string, key: string, value: string): Promise<{ key: string; value: string; updated_at: string }> {
		return this.put(`/projects/${encodeURIComponent(projectId)}/metadata/${encodeURIComponent(key)}`, { value });
	}
	deleteProjectMetadata(projectId: string, key: string): Promise<void> {
		return this.delete(`/projects/${encodeURIComponent(projectId)}/metadata/${encodeURIComponent(key)}`);
	}

	// ── Providers / Models ────────────────────────────────────────

	/** Fetches the DI-registered provider + model catalog from the backend.
	 *  Response shape matches OC's Provider SDK so dropped-in OC stores
	 *  (useConfigStore, useModelLists) consume it without adaptation —
	 *  `default.model` and `default.agent` come from the agent row marked
	 *  `is_default = true`. */
	getProviders(): Promise<{
		providers: {
			id:    string;
			name:  string;
			models: Record<string, {
				id:           string;
				name:         string;
				release_date: string | null;
				attachment:   { image: boolean; video: boolean; audio: boolean };
				reasoning:    { enabled: boolean; supported_efforts: string[] };
				cost:         { input: number; output: number; cache_read: number; cache_write: number };
				limit:        { context: number; output: number };
				experimental: boolean;
			}>;
		}[];
		default: { model: string | null; agent: string | null };
	}> {
		return this.get('/providers');
	}

	// ── Agents ─────────────────────────────────────────────────────

	listAgents():                Promise<unknown[]> { return this.get('/agents'); }
	getAgent(name: string):      Promise<unknown>   { return this.get(`/agents/${encodeURIComponent(name)}`); }
	createAgent(input: Record<string, unknown>): Promise<unknown> { return this.post('/agents', input); }
	updateAgent(name: string, input: Record<string, unknown>): Promise<unknown> {
		return this.patch(`/agents/${encodeURIComponent(name)}`, input);
	}
	deleteAgent(name: string):   Promise<void>      { return this.delete(`/agents/${encodeURIComponent(name)}`); }

	// ── Git: branches (CRUD) ──────────────────────────────────────

	createGitBranch(path: string, name: string, startPoint?: string): Promise<{ name: string; tip: string }> {
		return this.post('/git/branches', { path, name, start_point: startPoint });
	}
	renameGitBranch(path: string, oldName: string, newName: string): Promise<{ name: string; tip: string }> {
		return this.patch(`/git/branches/${encodeURIComponent(oldName)}`, { path, new_name: newName });
	}
	deleteGitBranch(path: string, name: string, force: boolean): Promise<void> {
		return this.delete(`/git/branches/${encodeURIComponent(name)}`, { path, force: force ? 'true' : 'false' });
	}

	// ── Git: stashes ──────────────────────────────────────────────

	listGitStashes(path: string): Promise<{ stashes: { index: number; ref: string; hash: string; message: string; at: string; relative_time: string }[] }> {
		return this.get('/git/stashes', { path });
	}
	saveGitStash(path: string, message: string | undefined, includeUntracked: boolean): Promise<{ success: boolean; created: boolean; ref: string | null; message: string; output: string }> {
		return this.post('/git/stashes', { path, message, include_untracked: includeUntracked });
	}
	countGitStashFiles(path: string, refs: string[]): Promise<{ counts: Record<string, number> }> {
		return this.post('/git/stashes/file-counts', { path, refs });
	}
	applyGitStash(path: string, stashRef: string): Promise<{ success: boolean; ref: string }> {
		return this.request('POST', `/git/stashes/${encodeURIComponent(stashRef)}/apply`, undefined, { path });
	}
	popGitStash(path: string, stashRef: string): Promise<{ success: boolean; ref: string }> {
		return this.request('POST', `/git/stashes/${encodeURIComponent(stashRef)}/pop`, undefined, { path });
	}
	dropGitStash(path: string, stashRef: string): Promise<{ success: boolean; ref: string }> {
		return this.delete(`/git/stashes/${encodeURIComponent(stashRef)}`, { path });
	}

	// ── Git: commit diff / conflict ───────────────────────────────

	getCommitFileDiff(path: string, commitSha: string, file: string): Promise<{ original: string; modified: string; is_binary: boolean }> {
		return this.get(`/git/commits/${encodeURIComponent(commitSha)}/file-diff`, { path, file });
	}
	getGitConflictDetails(path: string): Promise<{ operation: string; status_porcelain: string; unmerged_files: string[]; diff: string; head_info: string | null } | null> {
		return this.requestNullable('GET', '/git/conflicts', undefined, { path });
	}

	// ── Git: worktree bootstrap sidecar ───────────────────────────

	getWorktreeBootstrapStatus(worktreePath: string): Promise<{ status: 'pending' | 'ready' | 'failed' | string; error: string | null; updated_at: string }> {
		return this.get('/git/worktree-bootstrap', { path: worktreePath });
	}
	setWorktreeBootstrapStatus(worktreePath: string, status: 'pending' | 'ready' | 'failed', error?: string | null): Promise<unknown> {
		return this.put('/git/worktree-bootstrap', { worktree_path: worktreePath, status, error });
	}

	// ── Chat AI generation ─────────────────────────────────────────

	generateChat(chatId: string, body: { visible_prompt: string; hidden_prompt?: string; kind?: string; model?: string }): Promise<{ text: string; input_tokens: number; output_tokens: number }> {
		return this.post(`/chats/${encodeURIComponent(chatId)}/generate`, body);
	}

	/**
	 * Variant of <c>request</c> that converts 204 No Content into a Promise that resolves to null.
	 * Used by endpoints like <c>GET /api/git/conflicts</c> where "no conflict" is a valid empty response.
	 */
	private async requestNullable<T>(method: HttpMethod, path: string, body?: unknown, query?: HttpRequest['query']): Promise<T | null> {
		const res = await this.http.request({ method, path, body, query });
		if (res.status === 204) return null;
		if (res.status >= 200 && res.status < 300) return res.body as T;
		throw new LisRpcError(res.status, res.body);
	}

	// ── Chats ──────────────────────────────────────────────────────

	/**
	 * Lists every chat the server knows about. Newest <c>updated_at</c> first; archived
	 * rows are included with <c>archived_at</c> populated so the caller can bucket them.
	 * The sidebar pulls this on bootstrap (via <c>useGlobalSessionsStore.loadSessions</c>);
	 * subsequent live updates arrive through SSE on <c>/api/events</c>.
	 */
	listChats(): Promise<LisChatSummary[]> { return this.get('/chats'); }

	/**
	 * Create (or idempotently fetch) a chat. Pass `chatId` to use a deterministic id;
	 * omit it and the server mints one in the `web:{guid}` shape. Re-POSTing the same
	 * id returns the existing row with a 200 — the server treats it as idempotent.
	 *
	 * `projectExternalId` binds the chat under a project (404 if the project doesn't exist).
	 * `channel` defaults to `"web"` on the server when omitted.
	 */
	createChat(input: { chatId?: string; name?: string; projectExternalId?: string | null; channel?: string }): Promise<LisChatSummary> {
		return this.post('/chats', {
			chat_id:    input.chatId,
			name:       input.name,
			project_id: input.projectExternalId,
			channel:    input.channel,
		});
	}

	/** Rename a chat, or rebind its working directory. Empty `name` throws
	 *  (server-side ArgumentException → 400); pass `directory: ''` to CLEAR
	 *  the directory override and revert to project-root inheritance. */
	updateChat(chatId: string, patch: { name?: string; directory?: string | null }): Promise<LisChatSummary> {
		const body: Record<string, unknown> = {};
		if (patch.name      !== undefined) body.name      = patch.name;
		if (patch.directory !== undefined) body.directory = patch.directory ?? '';
		return this.patch(`/chats/${encodeURIComponent(chatId)}`, body);
	}

	/** Archive a chat — idempotent. Returns the row with `archived_at` populated. */
	archiveChat(chatId: string): Promise<LisChatSummary> {
		return this.post(`/chats/${encodeURIComponent(chatId)}/archive`);
	}

	/** Delete a chat and every message under it. Idempotent — deleting a missing chat returns 204. */
	deleteChat(chatId: string): Promise<void> {
		return this.delete(`/chats/${encodeURIComponent(chatId)}`);
	}

	/**
	 * Returns the chat's transcript, oldest-first within the limited window.
	 * Clamped 1-500 with default 100 server-side.
	 */
	getChatMessages(chatId: string, limit?: number): Promise<LisMessageSummary[]> {
		return this.get(`/chats/${encodeURIComponent(chatId)}/messages`, limit !== undefined ? { limit: String(limit) } : undefined);
	}

	/** Returns the agent effective for this chat (bound agent or system default). */
	getChatAgent(chatId: string): Promise<unknown> {
		return this.get(`/chats/${encodeURIComponent(chatId)}/agent`);
	}

	/** Rebind the chat to a different agent. Emits `chat_updated` on `/api/events`. */
	setChatAgent(chatId: string, agentName: string): Promise<unknown> {
		return this.patch(`/chats/${encodeURIComponent(chatId)}/agent`, { agent_name: agentName });
	}

	/**
	 * Save runtime overrides onto the chat's bound agent (or default when none).
	 * Emits `agent_updated` on `/api/events`.
	 */
	saveChatOverrides(chatId: string, overrides: { model?: string; effort?: string; tool_profile?: string }): Promise<unknown> {
		return this.patch(`/chats/${encodeURIComponent(chatId)}/overrides`, overrides);
	}

	/** Returns the context-window breakdown. `?force=true` bypasses the per-content token-count cache. */
	getChatContext(chatId: string, opts?: { force?: boolean }): Promise<unknown> {
		return this.get(`/chats/${encodeURIComponent(chatId)}/context`, opts?.force ? { force: 'true' } : undefined);
	}

	/** Abort the agent loop. Idempotent — no-op if nothing's running. */
	abortChat(chatId: string): Promise<void> {
		return this.requestNullable<void>('POST', `/chats/${encodeURIComponent(chatId)}/abort`).then(() => undefined);
	}

	/**
	 * Submit a user-side message. The agent loop runs asynchronously; open
	 * `/api/chats/{id}/stream` in parallel via {@link LisChatStreamChannel}
	 * to consume the SSE token stream. Returns the user-echo's message id.
	 */
	sendChatMessage(chatId: string, body: SendChatMessageBody): Promise<{ message_id: string }> {
		return this.post(`/chats/${encodeURIComponent(chatId)}/messages`, body);
	}

	// ── Permissions / Questions ────────────────────────────────────

	/** Deliver the user's decision for a pending tool-approval prompt. 204 on success. */
	respondToPermission(approvalId: string, decision: 'once' | 'always' | 'deny'): Promise<void> {
		return this.requestNullable<void>('POST', `/permissions/${encodeURIComponent(approvalId)}/respond`, { decision }).then(() => undefined);
	}

	/** Deliver the user's answers (or rejection) for a pending tool-question prompt. 204 on success. */
	respondToQuestion(questionId: string, body: { answers?: string[][]; rejected?: boolean }): Promise<void> {
		return this.requestNullable<void>('POST', `/questions/${encodeURIComponent(questionId)}/respond`, body).then(() => undefined);
	}

	// ── Session folders + pins ─────────────────────────────────────

	listSessionFolders(scope: string):                                Promise<unknown[]> { return this.get('/session-folders', { scope }); }
	createSessionFolder(scope: string, name: string, parentId?: string): Promise<unknown> {
		return this.post('/session-folders', { scope_key: scope, name, parent_id: parentId });
	}
	renameSessionFolder(id: string, name: string):                    Promise<unknown>   { return this.patch(`/session-folders/${encodeURIComponent(id)}`, { name }); }
	deleteSessionFolder(id: string):                                  Promise<void>      { return this.delete(`/session-folders/${encodeURIComponent(id)}`); }
	reorderSessionFolders(scope: string, ids: string[]):              Promise<void>      { return this.post('/session-folders/reorder', { scope_key: scope, ordered_external_ids: ids }); }
	addSessionsToFolder(id: string, chatIds: string[]):               Promise<unknown>   { return this.post(`/session-folders/${encodeURIComponent(id)}/items`, { chat_ids: chatIds }); }
	removeSessionFromFolder(id: string, chatId: string):              Promise<unknown>   { return this.delete(`/session-folders/${encodeURIComponent(id)}/items/${encodeURIComponent(chatId)}`); }
	pinChat(chatId: string):                                          Promise<void>      { return this.post(`/chats/${encodeURIComponent(chatId)}/pin`); }
	unpinChat(chatId: string):                                        Promise<void>      { return this.delete(`/chats/${encodeURIComponent(chatId)}/pin`); }

	// ── Git + worktrees ────────────────────────────────────────────

	getRepoStatus(path: string):                                      Promise<unknown>   { return this.get('/git/status',   { path }); }
	listBranches(path: string):                                       Promise<unknown>   { return this.get('/git/branches', { path }); }
	listWorktrees(projectPath: string):                               Promise<unknown[]> { return this.get('/worktrees', { project_path: projectPath }); }
	createWorktree(projectPath: string, newPath: string, branch: string): Promise<unknown> {
		return this.post('/worktrees', { project_path: projectPath, new_path: newPath, branch });
	}
	removeWorktree(projectPath: string, worktreePath: string, force = false): Promise<void> {
		return this.request<void>('DELETE', '/worktrees', { project_path: projectPath, worktree_path: worktreePath, force });
	}

	// ── Git identities (Stage 10 backend) ──────────────────────────

	listGitIdentities():                                              Promise<unknown[]> { return this.get('/git/identities'); }
	getGitIdentity(id: string):                                       Promise<unknown>   { return this.get(`/git/identities/${encodeURIComponent(id)}`); }
	createGitIdentity(input: Record<string, unknown>):                Promise<unknown>   { return this.post('/git/identities', input); }
	updateGitIdentity(id: string, input: Record<string, unknown>):    Promise<unknown>   { return this.patch(`/git/identities/${encodeURIComponent(id)}`, input); }
	deleteGitIdentity(id: string):                                    Promise<void>      { return this.delete(`/git/identities/${encodeURIComponent(id)}`); }
	getGlobalGitIdentity():                                           Promise<unknown>   { return this.get('/git/identities/global'); }
	getDefaultGitIdentity():                                          Promise<unknown>   { return this.get('/git/identities/default'); }
	setDefaultGitIdentity(externalId: string | null):                 Promise<void>      { return this.put('/git/identities/default', { external_id: externalId }); }

	// ── Scheduled tasks ────────────────────────────────────────────

	listScheduledTasksForProject(projectId: string):                  Promise<unknown[]> { return this.get(`/projects/${encodeURIComponent(projectId)}/scheduled-tasks`); }
	createScheduledTask(input: Record<string, unknown>):              Promise<unknown>   { return this.post('/scheduled-tasks', input); }
	updateScheduledTask(id: string, input: Record<string, unknown>):  Promise<unknown>   { return this.patch(`/scheduled-tasks/${encodeURIComponent(id)}`, input); }
	deleteScheduledTask(id: string):                                  Promise<void>      { return this.delete(`/scheduled-tasks/${encodeURIComponent(id)}`); }

	// ── Multi-run ──────────────────────────────────────────────────

	runMultiRun(input: { prompt: string; targets: { chat_id: string; agent_name?: string; model?: string }[] }): Promise<{ run_id: string }> {
		return this.post('/multi-run', input);
	}

	// ── GitHub ─────────────────────────────────────────────────────

	async getGitHubStatus(): Promise<GitHubStatusResponse> {
		const raw = await this.get<GitHubStatusWire>('/github/status');
		return remapGitHubStatus(raw);
	}
	async setGitHubToken(token: string): Promise<GitHubStatusResponse> {
		const raw = await this.put<GitHubStatusWire>('/github/token', { token });
		return remapGitHubStatus(raw);
	}
	clearGitHubToken():                                               Promise<void>      { return this.delete('/github/token'); }
	getPrStatus(keys: { owner: string; repo: string; branch: string }[]): Promise<unknown> {
		return this.post('/github/pr-status', { keys });
	}
	/** Resolve PR status by directory+branch (backend reads `.git/config` to find the GitHub coordinates). */
	getPrStatusByDirectory(keys: { directory: string; branch: string }[]): Promise<unknown> {
		return this.post('/github/pr-status/by-directory', { keys });
	}

	// ── Internals ──────────────────────────────────────────────────

	private async request<T>(method: HttpMethod, path: string, body?: unknown, query?: HttpRequest['query']): Promise<T> {
		const res = await this.http.request({ method, path, body, query });
		if (res.status >= 200 && res.status < 300) return res.body as T;
		throw new LisRpcError(res.status, res.body);
	}

	private onHostMessage(msg: unknown): void {
		if (!msg || typeof msg !== 'object') return;
		const m = msg as { type?: unknown };

		if (m.type === 'lis.init') {
			const init = msg as InitMessage;
			setLisLogVerbose(Boolean(init.verbose));
			lisLog.info(`rpc: lis.init received (origin=${init.baseOrigin || '∅'}, token=${init.token ? 'set' : 'missing'}, verbose=${Boolean(init.verbose)})`);
			this.setConfig(init.baseOrigin, init.token);
			return;
		}
		if (m.type === 'lis.config_update') {
			const upd = msg as ConfigUpdateMessage;
			setLisLogVerbose(Boolean(upd.verbose));
			lisLog.info(`rpc: lis.config_update received (origin=${upd.baseOrigin || '∅'}, token=${upd.token ? 'set' : 'missing'}, verbose=${Boolean(upd.verbose)})`);
			this.setConfig(upd.baseOrigin, upd.token);
			return;
		}
	}
}

// ── Chats: snake_case wire shape ─────────────────────────────────────────────

/**
 * Mirror of <c>Lis.Api.Schemas.Chats.ChatDto</c>. <c>pinned_at</c>/<c>archived_at</c>
 * are ISO-8601 strings when set, <c>null</c> otherwise. <c>project_id</c> is the
 * project's external id (not the FK).
 */
export interface LisChatSummary {
	chat_id:      string;
	name:         string | null;
	project_id:   string | null;
	pinned_at:    string | null;
	archived_at:  string | null;
	created_at:   string;
	updated_at:   string;
}

/**
 * Mirror of <c>Lis.Api.Schemas.Chats.MessageDto</c>. Tool-specific fields
 * (<c>tool_name</c>/<c>tool_args</c>/<c>tool_is_error</c>) are non-null only for
 * tool rows; <c>thinking_content</c> only for assistant rows.
 */
export interface LisMessageSummary {
	id:                      string;
	role:                    string;
	text:                    string | null;
	timestamp:               string;
	input_tokens:            number | null;
	output_tokens:           number | null;
	cache_read_tokens:       number | null;
	cache_creation_tokens:   number | null;
	thinking_tokens:         number | null;
	tool_name:               string | null;
	tool_args:               string | null;
	tool_is_error:           boolean | null;
	thinking_content:        string | null;
}

/** Body of `POST /api/chats/{id}/messages`. Either `text` OR `attachments` is required. */
export interface SendChatMessageBody {
	text?:          string | null;
	dispatch?:      'default' | 'immediate' | 'queue_only' | 'interrupt' | null;
	model?:         string | null;
	effort?:        string | null;
	tool_profile?:  string | null;
	thinking_mode?: 'budget' | 'adaptive' | 'auto' | null;
	attachments?:   { data_url: string; mime_type: string }[];
}

// ── GitHub status: snake_case wire ↔ camelCase response ──────────────────────

interface GitHubStatusWire {
	connected: boolean;
	login?:    string | null;
	scopes?:   string | null;
	error?:    string;
	user?:     { login: string; avatar_url?: string | null } | null;
	accounts?: { id: string; login: string; avatar_url?: string | null; is_active: boolean }[];
}

export interface GitHubStatusResponse {
	connected: boolean;
	login?:    string | null;
	scopes?:   string | null;
	error?:    string;
	user?:     { login: string; avatarUrl?: string | null } | null;
	accounts:  { id: string; login: string; avatarUrl?: string | null; isActive: boolean }[];
}

/**
 * The Lis backend speaks snake_case (project convention); the OC sidebar
 * components expect camelCase on the GitHub auth surface. This is the one
 * place the convention crosses — re-key here so consumers stay byte-identical
 * to OC originals.
 */
export function remapGitHubStatus(raw: GitHubStatusWire): GitHubStatusResponse {
	return {
		connected: raw.connected,
		login:     raw.login,
		scopes:    raw.scopes,
		error:     raw.error,
		user:      raw.user ? { login: raw.user.login, avatarUrl: raw.user.avatar_url ?? null } : null,
		accounts:  (raw.accounts ?? []).map((a) => ({
			id:        a.id,
			login:     a.login,
			avatarUrl: a.avatar_url ?? null,
			isActive:  a.is_active,
		})),
	};
}
