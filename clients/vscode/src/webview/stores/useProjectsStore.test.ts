import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { installProjectsSync, useProjectsStore, type ProjectEntry } from './useProjectsStore';

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
		new Response('null', { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

function makeProject(over: Partial<ProjectEntry> = {}): ProjectEntry {
	return {
		id:                'p1',
		path:              '/repo',
		label:             'Repo',
		color:             'blue',
		added_at:          '2026-01-01T00:00:00Z',
		created_at:        '2026-01-01T00:00:00Z',
		updated_at:        '2026-01-01T00:00:00Z',
		sidebar_collapsed: false,
		sort_index:        0,
		...over,
	};
}

function jsonResponse(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

describe('useProjectsStore', () => {
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
		useProjectsStore.setState({ projects: [], activeProjectId: null, loading: false, lastError: null });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('loadAll populates projects sorted by sort_index then created_at', async () => {
		fake.respond = () => jsonResponse([
			makeProject({ id: 'a', sort_index: 2 }),
			makeProject({ id: 'b', sort_index: 0 }),
			makeProject({ id: 'c', sort_index: 1 }),
		]);
		await useProjectsStore.getState().loadAll();

		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects');
		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['b', 'c', 'a']);
	});

	test('addProject POSTs and stores the created project', async () => {
		fake.respond = () => jsonResponse(makeProject({ id: 'p1' }), 201);
		const result = await useProjectsStore.getState().addProject('/repo', { label: 'Repo', color: 'purple' });

		const init = fake.calls[0].init!;
		expect(init.method).toBe('POST');
		expect(JSON.parse(init.body as string)).toEqual({ path: '/repo', label: 'Repo', color: 'purple' });
		expect(result?.id).toBe('p1');
		expect(useProjectsStore.getState().projects[0].id).toBe('p1');
	});

	test('updateProjectMeta maps camelCase keys to snake_case wire keys', async () => {
		fake.respond = () => jsonResponse(makeProject({ id: 'p1', label: 'New' }));
		await useProjectsStore.getState().updateProjectMeta('p1', {
			label:            'New',
			iconBackground:   '#fff',
			sidebarCollapsed: true,
		});

		const init = fake.calls[0].init!;
		expect(init.method).toBe('PATCH');
		expect(JSON.parse(init.body as string)).toEqual({ label: 'New', icon_background: '#fff', sidebar_collapsed: true });
	});

	test('removeProject delegates to DELETE and removes locally', async () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);
		fake.respond = () => new Response(null, { status: 204 });

		await useProjectsStore.getState().removeProject('p1');

		expect(fake.calls[0].init?.method).toBe('DELETE');
		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['p2']);
	});

	test('reorderProjects applies optimistic local order then POSTs the full id list', async () => {
		useProjectsStore.getState()._setFromServer([
			makeProject({ id: 'a', sort_index: 0 }),
			makeProject({ id: 'b', sort_index: 1 }),
			makeProject({ id: 'c', sort_index: 2 }),
		]);
		fake.respond = () => new Response(null, { status: 204 });

		const promise = useProjectsStore.getState().reorderProjects(0, 2);
		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['b', 'c', 'a']);

		await promise;
		const init = fake.calls[0].init!;
		expect(init.method).toBe('POST');
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects/reorder');
		expect(JSON.parse(init.body as string)).toEqual({ ordered_external_ids: ['b', 'c', 'a'] });
	});

	test('reorderProjects with invalid indices is a no-op', async () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'a' })]);
		await useProjectsStore.getState().reorderProjects(-1, 0);
		expect(fake.calls.length).toBe(0);
	});

	test('setActiveProject fires a touch call', async () => {
		fake.respond = () => jsonResponse(makeProject({ id: 'p1' }));
		useProjectsStore.getState().setActiveProject('p1');
		expect(useProjectsStore.getState().activeProjectId).toBe('p1');
		// Wait for the fire-and-forget touch to land.
		while (fake.calls.length === 0) await new Promise(r => setTimeout(r, 5));
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects/p1/touch');
		expect(fake.calls[0].init?.method).toBe('POST');
	});

	test('getActiveProject returns the matching project or null', () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' })]);
		useProjectsStore.getState().setActiveProjectIdOnly('p1');
		expect(useProjectsStore.getState().getActiveProject()?.id).toBe('p1');

		useProjectsStore.getState().setActiveProjectIdOnly('absent');
		expect(useProjectsStore.getState().getActiveProject()).toBeNull();
	});

	test('SSE project_created appends to local state via installProjectsSync', async () => {
		fake.respond = () => jsonResponse([]);
		const unsubscribe = installProjectsSync();
		// Let loadAll settle.
		await new Promise(r => setTimeout(r, 5));

		FakeEventSource.last!.fire('project_created', JSON.stringify({ project: makeProject({ id: 'sse-1' }) }));

		expect(useProjectsStore.getState().projects.map(p => p.id)).toContain('sse-1');
		unsubscribe();
	});

	test('SSE project_deleted removes from local state', async () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);
		fake.respond = () => jsonResponse([makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);
		const unsubscribe = installProjectsSync();
		await new Promise(r => setTimeout(r, 5));

		FakeEventSource.last!.fire('project_deleted', JSON.stringify({ project_id: 'p1' }));

		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['p2']);
		unsubscribe();
	});

	test('uploadProjectIcon encodes file as base64 and POSTs', async () => {
		fake.respond = () => jsonResponse(makeProject({ id: 'p1', icon_image_mime: 'image/png' }));
		const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'icon.png', { type: 'image/png' });
		const result = await useProjectsStore.getState().uploadProjectIcon('p1', file);

		const init = fake.calls[0].init!;
		expect(init.method).toBe('POST');
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects/p1/icon');
		const body = JSON.parse(init.body as string);
		expect(body.mime).toBe('image/png');
		expect(body.source).toBe('custom');
		expect(typeof body.blob_base64).toBe('string');
		expect(result.ok).toBe(true);
	});

	test('lastError is set on a failing add', async () => {
		fake.respond = () => jsonResponse({ error: 'path required' }, 400);
		const result = await useProjectsStore.getState().addProject('');
		expect(result).toBeNull();
		expect(useProjectsStore.getState().lastError).toContain('path required');
	});
});
