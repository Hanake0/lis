import { beforeEach, describe, expect, test } from 'bun:test';

import { LisEventChannel, type ChannelEvent, type EventSourceLike } from './event-channel';

class FakeEventSource implements EventSourceLike {
	static last: FakeEventSource | null = null;
	url: string;
	closed = false;
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	private handlers = new Map<string, ((ev: MessageEvent) => void)[]>();

	constructor(url: string) {
		this.url = url;
		FakeEventSource.last = this;
	}

	close(): void { this.closed = true; }

	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		if (!this.handlers.has(type)) this.handlers.set(type, []);
		this.handlers.get(type)!.push(handler);
	}

	/** Test helpers — simulate server pushes. */
	fire(type: string, data: string, id?: string): void {
		const list = this.handlers.get(type) ?? [];
		const ev = { data, lastEventId: id ?? '' } as unknown as MessageEvent;
		for (const h of list) h(ev);
	}

	signalOpen():  void { this.onopen?.(new Event('open')); }
	signalError(): void { this.onerror?.(new Event('error')); }
}

describe('LisEventChannel', () => {
	beforeEach(() => { FakeEventSource.last = null; });

	test('does not open when baseOrigin is empty', () => {
		const ch = new LisEventChannel('', '', (url) => new FakeEventSource(url));
		ch.open();
		expect(FakeEventSource.last).toBeNull();
		expect(ch.currentState).toBe('disconnected');
	});

	test('opens to /api/events with token in query string', () => {
		const ch = new LisEventChannel('http://localhost:3010', 'sekret', (url) => new FakeEventSource(url));
		ch.open();
		expect(FakeEventSource.last).not.toBeNull();
		expect(FakeEventSource.last!.url).toBe('http://localhost:3010/api/events?token=sekret');
		expect(ch.currentState).toBe('connecting');
	});

	test('omits the token param when no token is configured', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		ch.open();
		expect(FakeEventSource.last!.url).toBe('http://localhost:3010/api/events');
	});

	test('transitions to connected on onopen and disconnected on onerror', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		const states: string[] = [];
		ch.onState((s) => states.push(s));

		ch.open();
		FakeEventSource.last!.signalOpen();
		expect(ch.currentState).toBe('connected');

		FakeEventSource.last!.signalError();
		expect(ch.currentState).toBe('disconnected');

		expect(states).toContain('connecting');
		expect(states).toContain('connected');
		expect(states).toContain('disconnected');
	});

	test('parses JSON payloads on known events', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		const received: ChannelEvent[] = [];
		ch.onEvent((ev) => received.push(ev));
		ch.open();

		FakeEventSource.last!.fire('project_created', '{"project":{"id":"p1"}}', '42');

		expect(received).toHaveLength(1);
		expect(received[0].id).toBe('42');
		expect(received[0].event).toBe('project_created');
		expect((received[0].data as { project: { id: string } }).project.id).toBe('p1');
	});

	test('keeps data as raw string when JSON parse fails', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		const received: ChannelEvent[] = [];
		ch.onEvent((ev) => received.push(ev));
		ch.open();

		FakeEventSource.last!.fire('project_updated', 'not-json');
		expect(received[0].data).toBe('not-json');
	});

	test('close() stops the source and marks disconnected', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		ch.open();
		const src = FakeEventSource.last!;
		ch.close();
		expect(src.closed).toBe(true);
		expect(ch.currentState).toBe('disconnected');
	});

	test('updateConfig with different origin reopens the source', () => {
		const ch = new LisEventChannel('http://old:1234', 'a', (url) => new FakeEventSource(url));
		ch.open();
		const first = FakeEventSource.last!;
		ch.updateConfig('http://new:5678', 'b');
		const second = FakeEventSource.last!;

		expect(first.closed).toBe(true);
		expect(second).not.toBe(first);
		expect(second.url).toBe('http://new:5678/api/events?token=b');
	});

	test('updateConfig with same origin/token is a no-op', () => {
		const ch = new LisEventChannel('http://localhost:3010', 't', (url) => new FakeEventSource(url));
		ch.open();
		const first = FakeEventSource.last!;
		ch.updateConfig('http://localhost:3010', 't');
		expect(FakeEventSource.last).toBe(first); // unchanged
		expect(first.closed).toBe(false);
	});

	test('open() is idempotent — second call is a no-op while connected', () => {
		const ch = new LisEventChannel('http://localhost:3010', '', (url) => new FakeEventSource(url));
		ch.open();
		const first = FakeEventSource.last!;
		ch.open();
		expect(FakeEventSource.last).toBe(first);
	});
});
