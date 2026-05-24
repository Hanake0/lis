import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { installSessionPinnedSync, useSessionPinnedStore } from './useSessionPinnedStore';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_type: 'message', handler: (e: { data: unknown }) => void): void {
		this.handlers.push(handler);
	}
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

class FakeEventSource implements EventSourceLike {
	static last: FakeEventSource | null = null;
	url: string;
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	private handlers = new Map<string, ((ev: MessageEvent) => void)[]>();

	constructor(url: string) { this.url = url; FakeEventSource.last = this; }
	close(): void { /* noop */ }
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		if (!this.handlers.has(type)) this.handlers.set(type, []);
		this.handlers.get(type)!.push(handler);
	}
	fire(type: string, data: string): void {
		for (const h of this.handlers.get(type) ?? []) {
			h({ data, lastEventId: '' } as unknown as MessageEvent);
		}
	}
}

interface RecordedCall { url: string; init?: RequestInit }

class FakeFetch {
	calls: RecordedCall[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response = () =>
		new Response(null, { status: 204 });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

describe('useSessionPinnedStore', () => {
	const originalFetch = globalThis.fetch;
	let source: FakeMessageSource;
	let fake:   FakeFetch;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		source = new FakeMessageSource();
		fake = new FakeFetch();
		globalThis.fetch = fake.asFetch();
		FakeEventSource.last = null;
		LisRpcClient.install(source, { eventSourceFactory: (url) => new FakeEventSource(url) });
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
		useSessionPinnedStore.setState({ ids: new Set<string>(), lastError: null });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('pin() optimistically adds the chatId locally before the POST resolves', async () => {
		// Hold the response open so we can observe the optimistic state.
		let release: (() => void) | null = null;
		fake.respond = () => {
			return new Response(null, { status: 204 });
		};
		// Force the promise to remain pending until we explicitly resolve it.
		const gate = new Promise<void>(resolve => { release = resolve; });
		fake.respond = () => {
			// Wrap in a Response whose body promise is gated.
			return new Response(new ReadableStream({
				async start(controller) { await gate; controller.close(); },
			}), { status: 204 });
		};

		const promise = useSessionPinnedStore.getState().pin('chat-1');
		expect(useSessionPinnedStore.getState().ids.has('chat-1')).toBe(true);

		release!();
		await promise;
		expect(fake.calls.length).toBe(1);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/chats/chat-1/pin');
		expect(fake.calls[0].init?.method).toBe('POST');
		expect(useSessionPinnedStore.getState().ids.has('chat-1')).toBe(true);
		expect(useSessionPinnedStore.getState().lastError).toBeNull();
	});

	test('pin() rolls back the optimistic add when the REST call fails', async () => {
		fake.respond = () => new Response(JSON.stringify({ error: 'forbidden' }), {
			status: 403,
			headers: { 'content-type': 'application/json' },
		});

		await useSessionPinnedStore.getState().pin('chat-2');

		expect(useSessionPinnedStore.getState().ids.has('chat-2')).toBe(false);
		expect(useSessionPinnedStore.getState().lastError).toContain('forbidden');
	});

	test('unpin() optimistically removes and DELETEs', async () => {
		useSessionPinnedStore.setState({ ids: new Set(['chat-3']), lastError: null });
		fake.respond = () => new Response(null, { status: 204 });

		await useSessionPinnedStore.getState().unpin('chat-3');

		expect(useSessionPinnedStore.getState().ids.has('chat-3')).toBe(false);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/chats/chat-3/pin');
		expect(fake.calls[0].init?.method).toBe('DELETE');
	});

	test('unpin() rolls back the optimistic removal when the REST call fails', async () => {
		useSessionPinnedStore.setState({ ids: new Set(['chat-4']), lastError: null });
		fake.respond = () => new Response(JSON.stringify({ error: 'gone' }), {
			status: 500,
			headers: { 'content-type': 'application/json' },
		});

		await useSessionPinnedStore.getState().unpin('chat-4');

		expect(useSessionPinnedStore.getState().ids.has('chat-4')).toBe(true);
		expect(useSessionPinnedStore.getState().lastError).toBeTruthy();
	});

	test('toggle() flips between pinned and unpinned states', async () => {
		fake.respond = () => new Response(null, { status: 204 });

		await useSessionPinnedStore.getState().toggle('chat-5');
		expect(useSessionPinnedStore.getState().ids.has('chat-5')).toBe(true);
		expect(fake.calls[fake.calls.length - 1].init?.method).toBe('POST');

		await useSessionPinnedStore.getState().toggle('chat-5');
		expect(useSessionPinnedStore.getState().ids.has('chat-5')).toBe(false);
		expect(fake.calls[fake.calls.length - 1].init?.method).toBe('DELETE');
	});

	test('isPinned() reflects the current set membership', () => {
		expect(useSessionPinnedStore.getState().isPinned('chat-6')).toBe(false);
		useSessionPinnedStore.setState({ ids: new Set(['chat-6']) });
		expect(useSessionPinnedStore.getState().isPinned('chat-6')).toBe(true);
	});

	test('setIds() accepts a Set replacement', () => {
		useSessionPinnedStore.getState().setIds(new Set(['a', 'b']));
		expect(Array.from(useSessionPinnedStore.getState().ids).sort()).toEqual(['a', 'b']);
	});

	test('setIds() accepts an updater callback receiving the previous Set', () => {
		useSessionPinnedStore.setState({ ids: new Set(['x']) });
		useSessionPinnedStore.getState().setIds((prev) => {
			const next = new Set(prev);
			next.add('y');
			return next;
		});
		expect(Array.from(useSessionPinnedStore.getState().ids).sort()).toEqual(['x', 'y']);
	});

	test('setIds() short-circuits when the updater returns the same Set reference', () => {
		const original = new Set(['z']);
		useSessionPinnedStore.setState({ ids: original });
		useSessionPinnedStore.getState().setIds((prev) => prev);
		expect(useSessionPinnedStore.getState().ids).toBe(original);
	});

	test('SSE chat_pinned event applies via installSessionPinnedSync', () => {
		const unsubscribe = installSessionPinnedSync();

		FakeEventSource.last!.fire('chat_pinned', JSON.stringify({ chat_id: 'sse-pin' }));

		expect(useSessionPinnedStore.getState().ids.has('sse-pin')).toBe(true);
		unsubscribe();
	});

	test('SSE chat_unpinned event removes the entry', () => {
		useSessionPinnedStore.setState({ ids: new Set(['sse-existing']), lastError: null });
		const unsubscribe = installSessionPinnedSync();

		FakeEventSource.last!.fire('chat_unpinned', JSON.stringify({ chat_id: 'sse-existing' }));

		expect(useSessionPinnedStore.getState().ids.has('sse-existing')).toBe(false);
		unsubscribe();
	});

	test('SSE events with no chat_id payload are ignored without throwing', () => {
		const unsubscribe = installSessionPinnedSync();
		FakeEventSource.last!.fire('chat_pinned', JSON.stringify({}));
		expect(useSessionPinnedStore.getState().ids.size).toBe(0);
		unsubscribe();
	});

	test('SSE chat_unpinned for an id we never had is a no-op', () => {
		const unsubscribe = installSessionPinnedSync();
		FakeEventSource.last!.fire('chat_unpinned', JSON.stringify({ chat_id: 'never-pinned' }));
		expect(useSessionPinnedStore.getState().ids.size).toBe(0);
		unsubscribe();
	});
});
