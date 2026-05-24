// WebSocket wrapper for the Lis web channel.
// Web-extension compatible: uses the global WebSocket, no Node APIs.

export type ServerFrame =
	| { type: 'connected' }
	| { type: 'message';        chat_id: string; id: string; text: string; role: 'user' | 'assistant'; timestamp: string }
	| { type: 'typing';         chat_id: string }
	| { type: 'typing_stopped'; chat_id: string }
	| { type: 'reaction';       chat_id: string; id: string; emoji: string }
	| { type: 'error';          error: string; chat_id?: string }
	| { type: 'chat_list';      chats:    ChatSummary[] }
	| { type: 'chat_history';   chat_id: string; messages: MessageRow[] };

export interface ChatSummary  { chat_id: string; name?: string;   updated_at: string }
export interface MessageRow   { id: string; role: string; text?: string; timestamp: string }

export type DispatchMode = 'default' | 'immediate' | 'queue_only' | 'interrupt';

export type ClientFrame =
	| { type: 'message';    chat_id: string; text: string; dispatch?: DispatchMode }
	| { type: 'history';    chat_id: string; limit?: number }
	| { type: 'list_chats' };

export type ConnectionState = 'disconnected' | 'connecting' | 'connected';

type Listener<T> = (event: T) => void;

export class LisClient {
	private socket: WebSocket | null = null;
	private state:  ConnectionState  = 'disconnected';

	private frameListeners:   Listener<ServerFrame>[]     = [];
	private stateListeners:   Listener<ConnectionState>[] = [];

	private reconnectAttempt = 0;
	private reconnectTimer:  ReturnType<typeof setTimeout> | null = null;
	private explicitClose = false;

	constructor(private url: string, private token: string) {}

	// ── Public API ──────────────────────────────────────────────────

	get currentState(): ConnectionState { return this.state; }

	onFrame(fn: Listener<ServerFrame>):     void { this.frameListeners.push(fn); }
	onState(fn: Listener<ConnectionState>): void { this.stateListeners.push(fn); }

	connect(): void {
		if (this.state !== 'disconnected') return;
		this.explicitClose = false;
		this.openSocket();
	}

	disconnect(): void {
		this.explicitClose = true;
		if (this.reconnectTimer) { clearTimeout(this.reconnectTimer); this.reconnectTimer = null; }
		this.socket?.close();
		this.socket = null;
		this.setState('disconnected');
	}

	send(frame: ClientFrame): void {
		if (this.socket?.readyState !== WebSocket.OPEN) {
			console.warn('[Lis] dropping frame, socket not open:', frame);
			return;
		}
		this.socket.send(JSON.stringify(frame));
	}

	updateConfig(url: string, token: string): void {
		const changed = this.url !== url || this.token !== token;
		this.url   = url;
		this.token = token;
		if (changed && this.state !== 'disconnected') {
			this.disconnect();
			this.connect();
		}
	}

	// ── Internals ───────────────────────────────────────────────────

	private openSocket(): void {
		this.setState('connecting');

		const url = this.token
			? `${this.url}?token=${encodeURIComponent(this.token)}`
			: this.url;

		try {
			this.socket = new WebSocket(url);
		} catch (e) {
			console.error('[Lis] failed to open socket:', e);
			this.scheduleReconnect();
			return;
		}

		this.socket.onopen = () => {
			this.reconnectAttempt = 0;
			this.setState('connected');
		};

		this.socket.onmessage = ev => {
			let frame: ServerFrame;
			try {
				frame = JSON.parse(typeof ev.data === 'string' ? ev.data : '');
			} catch (e) {
				console.warn('[Lis] malformed frame:', ev.data, e);
				return;
			}
			for (const fn of this.frameListeners) fn(frame);
		};

		this.socket.onerror = e => {
			console.warn('[Lis] socket error:', e);
		};

		this.socket.onclose = () => {
			this.socket = null;
			this.setState('disconnected');
			if (!this.explicitClose) this.scheduleReconnect();
		};
	}

	private scheduleReconnect(): void {
		const delay = Math.min(30_000, 1000 * 2 ** Math.min(this.reconnectAttempt, 5));
		this.reconnectAttempt++;
		this.reconnectTimer = setTimeout(() => {
			this.reconnectTimer = null;
			this.openSocket();
		}, delay);
	}

	private setState(state: ConnectionState): void {
		if (this.state === state) return;
		this.state = state;
		for (const fn of this.stateListeners) fn(state);
	}
}
