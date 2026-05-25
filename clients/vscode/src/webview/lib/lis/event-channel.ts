/**
 * Thin wrapper around the browser-native `EventSource` for `/api/events`.
 *
 * Native EventSource handles:
 *   - Reconnect with the server-supplied `retry:` interval (with sane defaults).
 *   - `Last-Event-ID` header on reconnect (the browser tracks the last `id:` field).
 *   - Backpressure-free streaming.
 *
 * The auth token is passed as a `?token=` query param because EventSource cannot
 * set custom headers. Once the backend grows auth middleware, it must accept
 * both `Authorization: Bearer …` (used by REST) and `?token=` (used here).
 */

export type ConnectionState = 'connecting' | 'connected' | 'disconnected';

export interface ChannelEvent {
	id?:    string;
	event:  string;
	data:   unknown;
}

export type ChannelEventListener = (ev: ChannelEvent) => void;
export type ChannelStateListener = (state: ConnectionState) => void;

/** Minimal EventSource interface so tests can inject a fake. */
export interface EventSourceLike {
	close(): void;
	onopen:    ((ev: Event)        => void) | null;
	onerror:   ((ev: Event)        => void) | null;
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void;
}

export type EventSourceFactory = (url: string) => EventSourceLike;

const defaultFactory: EventSourceFactory = (url) => new EventSource(url);

/** Event names emitted by the backend on `/api/events`. Keep in sync with `Schemas/Events/*`. */
const KNOWN_EVENTS = [
	'project_created', 'project_updated', 'project_deleted', 'chat_moved',
	'folder_created',  'folder_updated',  'folder_deleted',
	'chat_created',    'chat_updated',    'chat_deleted',    'chat_archived',
	'chat_pinned',     'chat_unpinned',
	'agent_updated',
	'git_status_changed',
	'scheduled_task_created', 'scheduled_task_updated', 'scheduled_task_deleted', 'scheduled_task_run',
	'multi_run_progress',
] as const;

export class LisEventChannel {
	private baseOrigin: string;
	private token: string;
	private source: EventSourceLike | null = null;
	private state: ConnectionState = 'disconnected';
	private listeners      = new Set<ChannelEventListener>();
	private stateListeners = new Set<ChannelStateListener>();
	private wantOpen = false;
	private readonly factory: EventSourceFactory;

	constructor(baseOrigin = '', token = '', factory: EventSourceFactory = defaultFactory) {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
		this.factory = factory;
	}

	get currentState(): ConnectionState { return this.state; }

	onEvent(fn: ChannelEventListener): () => void {
		this.listeners.add(fn);
		return () => this.listeners.delete(fn);
	}

	onState(fn: ChannelStateListener): () => void {
		this.stateListeners.add(fn);
		try { fn(this.state); } catch { /* ignore */ }
		return () => this.stateListeners.delete(fn);
	}

	updateConfig(baseOrigin: string, token: string): void {
		const nextOrigin = stripTrailingSlash(baseOrigin);
		if (nextOrigin === this.baseOrigin && token === this.token) return;
		this.baseOrigin = nextOrigin;
		this.token = token;
		if (this.wantOpen) {
			this.close();
			this.open();
		}
	}

	open(): void {
		this.wantOpen = true;
		if (!this.baseOrigin) { this.setState('disconnected'); return; }
		if (this.source)       { return; } // already open

		this.setState('connecting');
		const params = new URLSearchParams();
		if (this.token) params.set('token', this.token);
		const url = `${this.baseOrigin}/api/events${params.size ? `?${params.toString()}` : ''}`;

		const src = this.factory(url);
		this.source = src;

		src.onopen  = () => this.setState('connected');
		src.onerror = () => {
			// EventSource auto-reconnects unless we close it. We surface 'disconnected'
			// for status feedback but do NOT call close — let the browser retry.
			this.setState('disconnected');
		};

		// Register handlers for every known event name; SSE's default 'message' channel
		// only fires when the server omits the `event:` field, which our backend never does.
		for (const name of KNOWN_EVENTS) {
			src.addEventListener(name, (msg) => this.dispatch(name, msg));
		}
		// Catch-all for events not in our static list.
		src.addEventListener('message', (msg) => this.dispatch('message', msg));
	}

	close(): void {
		this.wantOpen = false;
		if (!this.source) return;
		try { this.source.close(); } catch { /* ignore */ }
		this.source = null;
		this.setState('disconnected');
	}

	private dispatch(eventName: string, msg: MessageEvent): void {
		let data: unknown = msg.data;
		if (typeof msg.data === 'string' && msg.data.length > 0) {
			try { data = JSON.parse(msg.data); } catch { /* leave raw */ }
		}

		const ev: ChannelEvent = { id: msg.lastEventId || undefined, event: eventName, data };
		for (const fn of this.listeners) {
			try { fn(ev); } catch { /* swallow listener errors */ }
		}
	}

	private setState(state: ConnectionState): void {
		if (this.state === state) return;
		this.state = state;
		for (const fn of this.stateListeners) {
			try { fn(state); } catch { /* ignore */ }
		}
	}
}

function stripTrailingSlash(s: string): string { return s.replace(/\/+$/, ''); }
