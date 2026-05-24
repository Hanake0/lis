/**
 * Long-lived SSE reader for `/api/events`. Runs in the extension host (Node).
 * Webviews subscribe to relayed events via postMessage; they never open the SSE
 * connection themselves (CSP wouldn't allow it).
 *
 * Reconnects automatically with exponential backoff and replays missed events
 * via the `Last-Event-ID` header so consumers don't lose state across drops.
 */

export type ConnectionState = 'connecting' | 'connected' | 'disconnected';

export interface SseEvent {
	id?:    string;
	event:  string;
	data:   unknown;
}

export type EventListener = (ev: SseEvent) => void;
export type StateListener = (state: ConnectionState) => void;

export class LisEventStream {
	private baseOrigin: string;
	private token: string;
	private abort: AbortController | null = null;
	private lastEventId: string | null = null;
	private backoffMs = 1000;
	private state: ConnectionState = 'disconnected';
	private listeners = new Set<EventListener>();
	private stateListeners = new Set<StateListener>();
	private stopping = false;

	constructor(baseOrigin: string, token: string) {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
	}

	get currentState(): ConnectionState {
		return this.state;
	}

	onEvent(fn: EventListener): () => void {
		this.listeners.add(fn);
		return () => this.listeners.delete(fn);
	}

	onState(fn: StateListener): () => void {
		this.stateListeners.add(fn);
		// Immediately fire current state so subscribers don't need a separate getter.
		try { fn(this.state); } catch { /* swallow listener errors */ }
		return () => this.stateListeners.delete(fn);
	}

	updateConfig(baseOrigin: string, token: string): void {
		const next = stripTrailingSlash(baseOrigin);
		if (next === this.baseOrigin && token === this.token) return;
		this.baseOrigin = next;
		this.token = token;
		// Restart with new config.
		if (!this.stopping) {
			this.stop();
			this.start();
		}
	}

	start(): void {
		this.stopping = false;
		void this.runLoop();
	}

	stop(): void {
		this.stopping = true;
		this.abort?.abort();
		this.setState('disconnected');
	}

	private async runLoop(): Promise<void> {
		while (!this.stopping) {
			if (!this.baseOrigin) {
				this.setState('disconnected');
				return;
			}

			this.setState('connecting');
			this.abort = new AbortController();

			try {
				const url = `${this.baseOrigin}/api/events`;
				const headers: Record<string, string> = {
					Accept: 'text/event-stream',
				};
				if (this.token) headers.Authorization = `Bearer ${this.token}`;
				if (this.lastEventId) headers['Last-Event-ID'] = this.lastEventId;

				const response = await fetch(url, { method: 'GET', headers, signal: this.abort.signal });
				if (!response.ok || !response.body) {
					throw new Error(`SSE handshake failed: ${response.status}`);
				}

				this.setState('connected');
				this.backoffMs = 1000;
				await this.consume(response.body);
				// consume returned cleanly → server closed the stream. Reconnect after a back-off so
				// the loop can't spin if the server is in a "send one frame then EOF" state.
				if (this.stopping) return;
				this.setState('disconnected');
				await this.cancellableDelay(this.backoffMs);
				this.backoffMs = Math.min(this.backoffMs * 2, 30_000);
			} catch (err) {
				if (this.stopping) return;
				// Connection failed; back off then retry.
				this.setState('disconnected');
				await this.cancellableDelay(this.backoffMs);
				this.backoffMs = Math.min(this.backoffMs * 2, 30_000);
			}
		}
	}

	/** Sleep that resolves immediately when stop() is called. */
	private cancellableDelay(ms: number): Promise<void> {
		return new Promise<void>(resolve => {
			let resolved = false;
			const finish = () => { if (!resolved) { resolved = true; resolve(); } };
			const timer  = setTimeout(finish, ms);
			// Polling guard so stop() doesn't have to know about every in-flight delay.
			const poll = setInterval(() => {
				if (this.stopping) { clearTimeout(timer); clearInterval(poll); finish(); }
			}, 25);
			// Once the timer fires we don't need the poll anymore.
			setTimeout(() => clearInterval(poll), ms + 30);
		});
	}

	private async consume(body: ReadableStream<Uint8Array>): Promise<void> {
		const reader = body.getReader();
		const decoder = new TextDecoder();
		let buffer = '';

		while (!this.stopping) {
			const { value, done } = await reader.read();
			if (done) return;
			if (!value) continue;

			buffer += decoder.decode(value, { stream: true });

			// SSE frames terminate on a blank line (\n\n or \r\n\r\n).
			let sep: number;
			while ((sep = buffer.indexOf('\n\n')) >= 0 || (sep = buffer.indexOf('\r\n\r\n')) >= 0) {
				const length = buffer.startsWith('\r\n', sep) || buffer.charAt(sep) === '\r' ? 4 : 2;
				const raw = buffer.slice(0, sep).replace(/\r/g, '');
				buffer = buffer.slice(sep + length);
				if (!raw.trim()) continue;
				this.dispatch(raw);
			}
		}
	}

	private dispatch(rawFrame: string): void {
		let id:    string | undefined;
		let event: string = 'message';
		let data:  string = '';
		for (const line of rawFrame.split('\n')) {
			if (line.startsWith(':')) continue; // comment
			const colon = line.indexOf(':');
			if (colon < 0) continue;
			const field = line.slice(0, colon);
			// SSE allows a single space after the colon for cosmetics.
			const value = line.slice(colon + 1).replace(/^\s/, '');
			switch (field) {
				case 'id':    id = value; this.lastEventId = value; break;
				case 'event': event = value; break;
				case 'data':  data = data ? `${data}\n${value}` : value; break;
				default: break;
			}
		}
		if (!event) return;

		let parsed: unknown = data;
		try { parsed = data.length > 0 ? JSON.parse(data) : null; }
		catch { /* leave as raw string */ }

		const ev: SseEvent = { id, event, data: parsed };
		for (const fn of this.listeners) {
			try { fn(ev); } catch { /* swallow listener errors */ }
		}
	}

	private setState(state: ConnectionState): void {
		if (this.state === state) return;
		this.state = state;
		for (const fn of this.stateListeners) {
			try { fn(state); } catch { /* swallow listener errors */ }
		}
	}
}

function stripTrailingSlash(s: string): string {
	return s.replace(/\/+$/, '');
}
