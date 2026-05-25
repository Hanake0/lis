/**
 * Per-chat SSE token stream consumer. Backed by the same native EventSource
 * the global channel uses, but pointed at <c>/api/chats/{chatId}/stream</c> so
 * the server-side filter (Subject == "chat:{chatId}") drops unrelated traffic
 * before it hits the wire.
 *
 * The lifecycle is per-chat-per-turn — open it when the user opens the chat
 * view, close it when they navigate away or the turn ends. We don't auto-
 * reconnect on the chat stream the way the global channel does: chat events
 * are turn-scoped, so reconnecting after a long disconnect just yields a
 * cold stream until the next turn starts. The chat-view bootstraps with a
 * REST history fetch, and live updates resume from that point.
 *
 * Auth: same `?token=` query param trick as LisEventChannel — EventSource
 * cannot set custom headers, and the server middleware accepts both
 * `Authorization: Bearer …` and `?token=`.
 */

import type {
	ChannelEvent,
	ChannelEventListener,
	ChannelStateListener,
	ConnectionState,
	EventSourceFactory,
	EventSourceLike,
} from './event-channel';

/** Event names emitted on `/api/chats/{id}/stream`. Keep in sync with `ChatStreamEventNames`. */
const KNOWN_CHAT_STREAM_EVENTS = [
	'message',
	'chunk',
	'thinking_chunk',
	'tool_start',
	'tool_result',
	'typing',
	'typing_stopped',
	'reaction',
	'message_tokens',
	'permission_request',
	'question',
	'error',
] as const;

const defaultFactory: EventSourceFactory = (url) => new EventSource(url);

export class LisChatStreamChannel {
	private readonly baseOrigin: string;
	private readonly token:      string;
	private readonly chatId:     string;
	private readonly factory:    EventSourceFactory;
	private source: EventSourceLike | null = null;
	private state:  ConnectionState        = 'disconnected';
	private listeners      = new Set<ChannelEventListener>();
	private stateListeners = new Set<ChannelStateListener>();

	constructor(baseOrigin: string, token: string, chatId: string, factory: EventSourceFactory = defaultFactory) {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token      = token;
		this.chatId     = chatId;
		this.factory    = factory;
	}

	get currentState(): ConnectionState { return this.state; }
	get id(): string { return this.chatId; }

	onEvent(fn: ChannelEventListener): () => void {
		this.listeners.add(fn);
		return () => this.listeners.delete(fn);
	}

	onState(fn: ChannelStateListener): () => void {
		this.stateListeners.add(fn);
		try { fn(this.state); } catch { /* ignore */ }
		return () => this.stateListeners.delete(fn);
	}

	open(): void {
		if (!this.baseOrigin) { this.setState('disconnected'); return; }
		if (this.source)       { return; } // already open

		this.setState('connecting');
		const params = new URLSearchParams();
		if (this.token) params.set('token', this.token);
		const url = `${this.baseOrigin}/api/chats/${encodeURIComponent(this.chatId)}/stream${params.size ? `?${params.toString()}` : ''}`;

		const src = this.factory(url);
		this.source = src;

		src.onopen  = () => this.setState('connected');
		src.onerror = () => {
			// Unlike the global channel we DON'T leave it to auto-reconnect — chat
			// events are turn-scoped. Surface 'disconnected' and let the caller
			// decide whether to reopen (typically: on the next turn).
			this.setState('disconnected');
			this.close();
		};

		// `message` is already in KNOWN_CHAT_STREAM_EVENTS (and is the SSE default channel
		// when the server omits `event:`); no separate catch-all needed.
		for (const name of KNOWN_CHAT_STREAM_EVENTS) {
			src.addEventListener(name, (msg) => this.dispatch(name, msg));
		}
	}

	close(): void {
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
