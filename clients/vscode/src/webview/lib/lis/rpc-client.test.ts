import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import { LisRpcClient, LisRpcError, type MessageSource } from './rpc-client';
import type { EventSourceLike } from './event-channel';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_type: 'message', handler: (e: { data: unknown }) => void): void {
		this.handlers.push(handler);
	}
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

class FakeEventSource implements EventSourceLike {
	url: string;
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	private handlers = new Map<string, ((ev: MessageEvent) => void)[]>();
	constructor(url: string) { this.url = url; FakeEventSource.last = this; }
	static last: FakeEventSource | null = null;
	close(): void { /* noop */ }
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		if (!this.handlers.has(type)) this.handlers.set(type, []);
		this.handlers.get(type)!.push(handler);
	}
	fire(type: string, data: string, id?: string): void {
		for (const h of this.handlers.get(type) ?? []) {
			h({ data, lastEventId: id ?? '' } as unknown as MessageEvent);
		}
	}
}

interface RecordedCall { url: string; init?: RequestInit }

class FakeFetch {
	calls: RecordedCall[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response = () =>
		new Response('null', { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

describe('LisRpcClient', () => {
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
		// Hand the client its config (the extension host normally does this).
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('lis.init applies config and opens the event channel', () => {
		expect(FakeEventSource.last).not.toBeNull();
		expect(FakeEventSource.last!.url).toBe('http://localhost:3010/api/events?token=tok');
	});

	test('lis.config_update swaps origin + reopens event channel', () => {
		const first = FakeEventSource.last!;
		source.fire({ type: 'lis.config_update', baseOrigin: 'http://new:9999', token: 'other' });
		const second = FakeEventSource.last!;
		expect(second).not.toBe(first);
		expect(second.url).toBe('http://new:9999/api/events?token=other');
	});

	test('listProjects uses fetch to GET /api/projects', async () => {
		fake.respond = () => new Response('[{"id":"p1"}]', { status: 200, headers: { 'content-type': 'application/json' } });
		const result = await LisRpcClient.instance().listProjects() as { id: string }[];
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects');
		expect(result[0].id).toBe('p1');
	});

	test('createProject POSTs JSON body', async () => {
		fake.respond = () => new Response('{"id":"abc"}', { status: 201, headers: { 'content-type': 'application/json' } });
		const result = await LisRpcClient.instance().createProject({ path: '/repo' }) as { id: string };
		expect(fake.calls[0].init?.method).toBe('POST');
		expect(fake.calls[0].init?.body).toBe('{"path":"/repo"}');
		expect(result.id).toBe('abc');
	});

	test('non-2xx response rejects with LisRpcError', async () => {
		fake.respond = () => new Response('{"error":"not found"}', { status: 404, headers: { 'content-type': 'application/json' } });
		try {
			await LisRpcClient.instance().getProject('absent');
			throw new Error('expected throw');
		} catch (err) {
			expect(err).toBeInstanceOf(LisRpcError);
			expect((err as LisRpcError).status).toBe(404);
		}
	});

	test('URL-unsafe ids are encoded in path segments', async () => {
		await LisRpcClient.instance().getProject('weird/id?x=1').catch(() => {});
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects/weird%2Fid%3Fx%3D1');
	});

	test('listSessionFolders passes scope as a query param', async () => {
		await LisRpcClient.instance().listSessionFolders('dir:/a').catch(() => {});
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/session-folders?scope=dir%3A%2Fa');
	});

	test('removeWorktree sends a DELETE with a JSON body', async () => {
		await LisRpcClient.instance().removeWorktree('/proj', '/wt', true).catch(() => {});
		const init = fake.calls[0].init!;
		expect(init.method).toBe('DELETE');
		expect(init.body).toBe('{"project_path":"/proj","worktree_path":"/wt","force":true}');
	});

	test('moveChat to project posts the right body', async () => {
		await LisRpcClient.instance().moveChat('chat-1', 'proj-x').catch(() => {});
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/chats/chat-1/move');
		expect(fake.calls[0].init?.body).toBe('{"project_external_id":"proj-x"}');
	});

	test('moveChat detach sends null project id', async () => {
		await LisRpcClient.instance().moveChat('chat-1', null).catch(() => {});
		expect(fake.calls[0].init?.body).toBe('{"project_external_id":null}');
	});

	test('onSse fans out events from the channel', () => {
		const received: { event: string; data: unknown }[] = [];
		LisRpcClient.instance().onSse((ev) => received.push({ event: ev.event, data: ev.data }));

		FakeEventSource.last!.fire('project_created', '{"project":{"id":"p1"}}');

		expect(received).toHaveLength(1);
		expect(received[0].event).toBe('project_created');
		expect((received[0].data as { project: { id: string } }).project.id).toBe('p1');
	});

	test('install is idempotent', () => {
		const a = LisRpcClient.install(source);
		const b = LisRpcClient.install(source);
		expect(a).toBe(b);
	});

	test('instance throws before install', () => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		expect(() => LisRpcClient.instance()).toThrow();
	});
});
