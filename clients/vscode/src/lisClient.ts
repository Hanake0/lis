// WebSocket wrapper for the Lis web channel.
// Web-extension compatible: uses the global WebSocket, no Node APIs.

import type { Logger } from './logger';
import type { ClientFrame, ConnectionState, ServerFrame } from './types';

type Listener<T> = (event: T) => void;

export class LisClient {
	private socket: WebSocket | null = null;
	private state:  ConnectionState  = 'disconnected';

	private frameListeners:   Listener<ServerFrame>[]     = [];
	private stateListeners:   Listener<ConnectionState>[] = [];

	private reconnectAttempt = 0;
	private reconnectTimer:  ReturnType<typeof setTimeout> | null = null;
	private explicitClose = false;

	constructor(private url: string, private token: string, private readonly logger?: Logger) {}

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
			this.logger?.warn(`dropping frame, socket not open: ${frame.type}`);
			return;
		}
		this.socket.send(JSON.stringify(frame));
		this.logger?.frame('send', frame);
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

		this.logger?.info(`connecting to ${this.url}${this.token ? ' (token set)' : ' (no token!)'}`);

		try {
			this.socket = new WebSocket(url);
		} catch (e) {
			this.logger?.error('failed to open socket', e);
			this.scheduleReconnect();
			return;
		}

		this.socket.onopen = () => {
			this.reconnectAttempt = 0;
			this.setState('connected');
			this.logger?.info('connected');
		};

		this.socket.onmessage = ev => {
			let frame: ServerFrame;
			try {
				frame = JSON.parse(typeof ev.data === 'string' ? ev.data : '');
			} catch (e) {
				this.logger?.warn(`malformed frame: ${typeof ev.data === 'string' ? ev.data : '<binary>'}`, e);
				return;
			}
			this.logger?.frame('recv', frame);
			for (const fn of this.frameListeners) fn(frame);
		};

		this.socket.onerror = () => {
			// Browser WebSocket onerror events carry no useful detail; the close event has the why.
			this.logger?.warn('socket error');
		};

		this.socket.onclose = ev => {
			this.socket = null;
			this.setState('disconnected');
			this.logger?.info(`disconnected (code=${ev.code}${ev.reason ? `, reason="${ev.reason}"` : ''})`);
			if (!this.explicitClose) this.scheduleReconnect();
		};
	}

	private scheduleReconnect(): void {
		const delay = Math.min(30_000, 1000 * 2 ** Math.min(this.reconnectAttempt, 5));
		this.reconnectAttempt++;
		this.logger?.info(`reconnecting in ${delay}ms (attempt ${this.reconnectAttempt})`);
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
