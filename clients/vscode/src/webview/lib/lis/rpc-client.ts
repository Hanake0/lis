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
}

interface ConfigUpdateMessage {
	type:       'lis.config_update';
	baseOrigin: string;
	token:      string;
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

	private constructor(source: MessageSource, eventSourceFactory?: EventSourceFactory) {
		this.http    = new LisHttpClient();
		this.channel = new LisEventChannel('', '', eventSourceFactory);

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

	/** Internals — exposed for tests; production code goes through `install` + postMessage. */
	setConfig(baseOrigin: string, token: string): void {
		this.http.updateConfig(baseOrigin, token);
		this.channel.updateConfig(baseOrigin, token);
		// Open (or reopen if config changed) the SSE channel.
		if (baseOrigin) this.channel.open();
		else            this.channel.close();
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

	getGitHubStatus():                                                Promise<unknown>   { return this.get('/github/status'); }
	setGitHubToken(token: string):                                    Promise<unknown>   { return this.put('/github/token', { token }); }
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
			this.setConfig(init.baseOrigin, init.token);
			return;
		}
		if (m.type === 'lis.config_update') {
			const upd = msg as ConfigUpdateMessage;
			this.setConfig(upd.baseOrigin, upd.token);
			return;
		}
	}
}
