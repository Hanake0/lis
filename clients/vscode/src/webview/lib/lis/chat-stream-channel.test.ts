import { beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from './event-channel';
import { LisChatStreamChannel } from './chat-stream-channel';

interface Recorded {
	type:    string;
	handler: (ev: MessageEvent) => void;
}

class FakeEventSource implements EventSourceLike {
	static lastUrl:  string  | null = null;
	static lastInstance: FakeEventSource | null = null;
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	closed = false;
	readonly handlers: Recorded[] = [];
	constructor(url: string) {
		FakeEventSource.lastUrl      = url;
		FakeEventSource.lastInstance = this;
	}
	close(): void { this.closed = true; }
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		this.handlers.push({ type, handler });
	}
	fire(type: string, data: unknown, id?: string): void {
		for (const h of this.handlers) {
			if (h.type === type) h.handler({ data, lastEventId: id ?? '' } as MessageEvent);
		}
	}
}

describe('LisChatStreamChannel', () => {
	beforeEach(() => {
		FakeEventSource.lastUrl      = null;
		FakeEventSource.lastInstance = null;
	});

	test('open() points the EventSource at /api/chats/{id}/stream with the auth token', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok-1', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		expect(FakeEventSource.lastUrl).toBe('http://localhost:3010/api/chats/c1/stream?token=tok-1');
	});

	test('chat ids are URL-encoded so colons and slashes survive the wire', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'web:abc/123', (u) => new FakeEventSource(u));
		ch.open();
		expect(FakeEventSource.lastUrl).toBe('http://localhost:3010/api/chats/web%3Aabc%2F123/stream?token=tok');
	});

	test('open() with no token omits the ?token= query param', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', '', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		expect(FakeEventSource.lastUrl).toBe('http://localhost:3010/api/chats/c1/stream');
	});

	test('open() with no baseOrigin is a noop — leaves state=disconnected and no EventSource', () => {
		const ch = new LisChatStreamChannel('', 'tok', 'c1', (u) => new FakeEventSource(u));
		const states: string[] = [];
		ch.onState((s) => states.push(s));
		ch.open();
		expect(FakeEventSource.lastInstance).toBeNull();
		// Initial snapshot only — `setState` is a no-op when the new state matches the
		// current one, so the second `setState('disconnected')` inside open() doesn't fire.
		expect(states).toEqual(['disconnected']);
	});

	test('open() twice is a noop on the second call', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		const first = FakeEventSource.lastInstance;
		ch.open();
		expect(FakeEventSource.lastInstance).toBe(first);
	});

	test('events are parsed as JSON and dispatched to listeners with event name + id', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		const received: { event: string; data: unknown; id?: string }[] = [];
		ch.onEvent((ev) => received.push(ev));

		FakeEventSource.lastInstance!.fire('chunk', '{"delta":"hi","done":false}', '7');
		expect(received).toEqual([{ event: 'chunk', data: { delta: 'hi', done: false }, id: '7' }]);
	});

	test('non-JSON data is forwarded raw without throwing', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		const received: unknown[] = [];
		ch.onEvent((ev) => received.push(ev.data));
		FakeEventSource.lastInstance!.fire('chunk', 'not-json');
		expect(received[0]).toBe('not-json');
	});

	test('listener throws do not break the dispatch fan-out to other listeners', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		const received: string[] = [];
		ch.onEvent(() => { throw new Error('boom'); });
		ch.onEvent((ev) => received.push(ev.event));
		FakeEventSource.lastInstance!.fire('typing', '{"chat_id":"c1"}');
		expect(received).toEqual(['typing']);
	});

	test('every known chat-stream event registers a handler', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		const types = FakeEventSource.lastInstance!.handlers.map((h) => h.type).sort();
		expect(types).toEqual([
			'chunk', 'error', 'message', 'message_tokens', 'permission_request',
			'question', 'reaction', 'thinking_chunk', 'tool_result', 'tool_start',
			'typing', 'typing_stopped',
		]);
	});

	test('onopen flips state to connected, onerror flips to disconnected AND closes the source', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		const states: string[] = [];
		ch.onState((s) => states.push(s));

		ch.open();
		FakeEventSource.lastInstance!.onopen!(new Event('open'));
		FakeEventSource.lastInstance!.onerror!(new Event('error'));

		// Initial snapshot + connecting (open) + connected (onopen) + disconnected (onerror).
		expect(states).toEqual(['disconnected', 'connecting', 'connected', 'disconnected']);
		expect(FakeEventSource.lastInstance!.closed).toBe(true);
	});

	test('close() unsubscribes the EventSource and flips state', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		ch.open();
		FakeEventSource.lastInstance!.onopen!(new Event('open'));
		ch.close();
		expect(FakeEventSource.lastInstance!.closed).toBe(true);
		expect(ch.currentState).toBe('disconnected');
	});

	test('close() before open() is a noop', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c1', (u) => new FakeEventSource(u));
		expect(() => ch.close()).not.toThrow();
	});

	test('id exposes the bound chat id', () => {
		const ch = new LisChatStreamChannel('http://localhost:3010', 'tok', 'c-bound', (u) => new FakeEventSource(u));
		expect(ch.id).toBe('c-bound');
	});
});
