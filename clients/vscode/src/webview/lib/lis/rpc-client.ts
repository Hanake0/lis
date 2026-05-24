/**
 * Webview-side RPC adapter for the Lis backend. The webview is sandboxed (CSP),
 * so it can't fetch URLs directly. Instead we post an `http_request` envelope
 * to the extension host and await the matching `http_response`.
 *
 * Method signatures intentionally mirror the slice of OC's `OpencodeService`
 * surface we actually use, so existing OC stores can drop in without changes.
 * Each backend wire DTO is snake_case; the methods here pass-through unchanged.
 */

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

interface HostMessage {
	type:        'http_request';
	request_id:  string;
	method:      HttpMethod;
	path:        string;
	query?:      Record<string, string | number | boolean | null | undefined>;
	body?:       unknown;
}

interface HostResponse {
	type:       'http_response';
	request_id: string;
	status:     number;
	body:       unknown;
}

interface SseMessage {
	type:    'sse_event';
	source:  'events' | 'chat';
	chat_id?: string;
	event:   string;
	data:    unknown;
	id?:     string;
}

interface ConnectionStateMessage {
	type:   'connection_state';
	events: 'connecting' | 'connected' | 'disconnected';
}

interface VsCodeApi {
	postMessage(msg: unknown): void;
}

/** Minimal subset of EventTarget that LisRpcClient needs to subscribe to messages.
 *  In the real webview this is `window`; in tests a small fake can stand in. */
export interface MessageSource {
	addEventListener(type: 'message', handler: (e: { data: unknown }) => void): void;
	removeEventListener?(type: 'message', handler: (e: { data: unknown }) => void): void;
}

type SseListener = (event: string, data: unknown, meta: { source: 'events' | 'chat'; chat_id?: string; id?: string }) => void;
type StateListener = (state: 'connecting' | 'connected' | 'disconnected') => void;

export class LisRpcError extends Error {
	constructor(public readonly status: number, public readonly body: unknown) {
		super(typeof body === 'object' && body !== null && 'error' in body ? String((body as { error: unknown }).error) : `request failed (${status})`);
		this.name = 'LisRpcError';
	}
}

export class LisRpcClient {
	private static singleton: LisRpcClient | null = null;
	private readonly pending = new Map<string, { resolve: (body: unknown) => void; reject: (err: Error) => void }>();
	private readonly sseListeners = new Set<SseListener>();
	private readonly stateListeners = new Set<StateListener>();

	private constructor(private readonly vscode: VsCodeApi, source: MessageSource) {
		source.addEventListener('message', (e) => this.onMessage(e.data));
	}

	/** Wire the singleton once, near app boot. Subsequent calls return the existing instance.
	 *  `source` defaults to the global `window` (real webview); tests can pass a mock. */
	static install(vscode: VsCodeApi, source?: MessageSource): LisRpcClient {
		const target = source ?? (typeof window !== 'undefined' ? window : undefined);
		if (!target) throw new Error('LisRpcClient.install: no MessageSource available (pass one explicitly in non-browser environments)');
		LisRpcClient.singleton ??= new LisRpcClient(vscode, target);
		return LisRpcClient.singleton;
	}

	/** Get the installed client, or throw if `install` was never called. */
	static instance(): LisRpcClient {
		if (!LisRpcClient.singleton) throw new Error('LisRpcClient.install() must be called before use');
		return LisRpcClient.singleton;
	}

	/** Subscribe to all SSE events. Returns an unsubscribe function. */
	onSse(fn: SseListener): () => void {
		this.sseListeners.add(fn);
		return () => this.sseListeners.delete(fn);
	}

	/** Subscribe to connection-state transitions on the global event stream. */
	onState(fn: StateListener): () => void {
		this.stateListeners.add(fn);
		return () => this.stateListeners.delete(fn);
	}

	// ── Generic helpers ────────────────────────────────────────────

	get<T>   (path: string, query?: HostMessage['query']):   Promise<T> { return this.request<T>('GET',    path, undefined, query); }
	delete<T>(path: string, query?: HostMessage['query']):   Promise<T> { return this.request<T>('DELETE', path, undefined, query); }
	post<T>  (path: string, body?: unknown):                 Promise<T> { return this.request<T>('POST',   path, body); }
	put<T>   (path: string, body?: unknown):                 Promise<T> { return this.request<T>('PUT',    path, body); }
	patch<T> (path: string, body?: unknown):                 Promise<T> { return this.request<T>('PATCH',  path, body); }

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
		// DELETE with a JSON body; LisHttpClient handles the body serialisation.
		return this.request<void>('DELETE', '/worktrees', { project_path: projectPath, worktree_path: worktreePath, force });
	}

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

	getGitHubStatus():                                                Promise<unknown>   { return this.get('/github/status'); }
	setGitHubToken(token: string):                                    Promise<unknown>   { return this.put('/github/token', { token }); }
	clearGitHubToken():                                               Promise<void>      { return this.delete('/github/token'); }
	getPrStatus(keys: { owner: string; repo: string; branch: string }[]): Promise<unknown> {
		return this.post('/github/pr-status', { keys });
	}

	// ── Internals ──────────────────────────────────────────────────

	private request<T>(method: HttpMethod, path: string, body?: unknown, query?: HostMessage['query']): Promise<T> {
		return new Promise<T>((resolve, reject) => {
			const requestId = crypto.randomUUID();
			this.pending.set(requestId, {
				resolve: (b) => resolve(b as T),
				reject,
			});

			const envelope: HostMessage = { type: 'http_request', request_id: requestId, method, path, body, query };
			this.vscode.postMessage(envelope);
		});
	}

	private onMessage(msg: unknown): void {
		if (!msg || typeof msg !== 'object') return;
		const m = msg as { type?: unknown };

		if (m.type === 'http_response') {
			const r = msg as HostResponse;
			const pending = this.pending.get(r.request_id);
			if (!pending) return;
			this.pending.delete(r.request_id);
			if (r.status >= 200 && r.status < 300) pending.resolve(r.body);
			else                                    pending.reject(new LisRpcError(r.status, r.body));
			return;
		}

		if (m.type === 'sse_event') {
			const e = msg as SseMessage;
			for (const fn of this.sseListeners) {
				try { fn(e.event, e.data, { source: e.source, chat_id: e.chat_id, id: e.id }); } catch { /* ignore */ }
			}
			return;
		}

		if (m.type === 'connection_state') {
			const c = msg as ConnectionStateMessage;
			for (const fn of this.stateListeners) {
				try { fn(c.events); } catch { /* ignore */ }
			}
			return;
		}
	}
}
