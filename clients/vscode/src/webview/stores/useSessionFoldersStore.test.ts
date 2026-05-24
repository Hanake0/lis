import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	installSessionFoldersSync,
	useSessionFoldersStore,
	type SessionFolderDto,
} from './useSessionFoldersStore';

// ── Test doubles ────────────────────────────────────────────────────────────

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

function makeFolderDto(over: Partial<SessionFolderDto> = {}): SessionFolderDto {
	return {
		id:         'f1',
		scope_key:  'dir:/repo',
		name:       'Inbox',
		parent_id:  null,
		sort_index: 0,
		items:      [],
		created_at: '2026-01-01T00:00:00Z',
		updated_at: '2026-01-01T00:00:00Z',
		...over,
	};
}

function jsonResponse(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

/** Run all queued microtasks so optimistic background tasks settle. */
async function flushMicrotasks(): Promise<void> {
	for (let i = 0; i < 4; i++) await Promise.resolve();
}

// ── Tests ───────────────────────────────────────────────────────────────────

describe('useSessionFoldersStore', () => {
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
		useSessionFoldersStore.setState({
			foldersMap:         {},
			collapsedFolderIds: new Set<string>(),
			scopesLoaded:       new Set<string>(),
			loading:            false,
			lastError:          null,
		});
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('loadScope fetches folders for a scope and stores them', async () => {
		fake.respond = () => jsonResponse([
			makeFolderDto({ id: 'a', name: 'Alpha' }),
			makeFolderDto({ id: 'b', name: 'Beta', items: [{ chat_id: 'c1', sort_index: 0, added_at: '2026-01-01T00:00:00Z' }] }),
		]);

		await useSessionFoldersStore.getState().loadScope('dir:/repo');

		expect(fake.calls[0].url).toBe('http://localhost:3010/api/session-folders?scope=dir%3A%2Frepo');
		const folders = useSessionFoldersStore.getState().foldersMap['dir:/repo'];
		expect(folders.map(f => f.id)).toEqual(['a', 'b']);
		expect(folders[1].sessionIds).toEqual(['c1']);
		expect(useSessionFoldersStore.getState().scopesLoaded.has('dir:/repo')).toBe(true);
	});

	test('loadScope is idempotent — second call short-circuits without re-fetching', async () => {
		fake.respond = () => jsonResponse([]);
		await useSessionFoldersStore.getState().loadScope('dir:/repo');
		await useSessionFoldersStore.getState().loadScope('dir:/repo');
		expect(fake.calls.length).toBe(1);
	});

	test('getFoldersForScope lazily fires loadScope on first read for an unseen scope', async () => {
		fake.respond = () => jsonResponse([makeFolderDto({ id: 'lazy' })]);
		const initial = useSessionFoldersStore.getState().getFoldersForScope('dir:/lazy');
		expect(initial).toEqual([]);
		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		const folders = useSessionFoldersStore.getState().foldersMap['dir:/lazy'];
		expect(folders.map(f => f.id)).toEqual(['lazy']);
	});

	test('createFolder returns the local folder synchronously then reconciles with the server id', async () => {
		fake.respond = () => jsonResponse(makeFolderDto({ id: 'server-id', name: 'New' }), 201);

		const local = useSessionFoldersStore.getState().createFolder('dir:/repo', 'New');
		expect(local.id.startsWith('local_')).toBe(true);
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'].map(f => f.id)).toEqual([local.id]);

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));

		const finalIds = useSessionFoldersStore.getState().foldersMap['dir:/repo'].map(f => f.id);
		expect(finalIds).toEqual(['server-id']);
		expect(fake.calls[0].init?.method).toBe('POST');
		expect(JSON.parse(fake.calls[0].init!.body as string)).toEqual({ scope_key: 'dir:/repo', name: 'New', parent_id: undefined });
	});

	test('createFolder rolls back the optimistic insert when the POST fails', async () => {
		fake.respond = () => jsonResponse({ error: 'forbidden' }, 403);
		useSessionFoldersStore.getState().createFolder('dir:/repo', 'Broken');
		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));

		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo']).toEqual([]);
		expect(useSessionFoldersStore.getState().lastError).toContain('forbidden');
	});

	test('renameFolder optimistically renames then PATCHes', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'f1', name: 'Old' }));
		let patchCalled = false;
		fake.respond = ({ url, init }) => {
			if (init?.method === 'PATCH' && url.endsWith('/session-folders/f1')) {
				patchCalled = true;
				return jsonResponse(makeFolderDto({ id: 'f1', name: 'New' }));
			}
			return jsonResponse(null);
		};

		useSessionFoldersStore.getState().renameFolder('dir:/repo', 'f1', 'New');
		// Optimistic name update is synchronous.
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].name).toBe('New');

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		expect(patchCalled).toBe(true);
	});

	test('renameFolder rolls back when the PATCH fails', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'f2', name: 'Stable' }));
		fake.respond = () => jsonResponse({ error: 'denied' }, 500);

		useSessionFoldersStore.getState().renameFolder('dir:/repo', 'f2', 'Renamed');
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].name).toBe('Renamed');

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].name).toBe('Stable');
	});

	test('deleteFolder removes the folder and its descendants locally then DELETEs the top one', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'parent' }));
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'child', parent_id: 'parent', name: 'Child' }));
		fake.respond = () => new Response(null, { status: 204 });

		useSessionFoldersStore.getState().deleteFolder('dir:/repo', 'parent');
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo']).toEqual([]);

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		// Only the parent gets a DELETE — backend orphans children server-side.
		const deletes = fake.calls.filter(c => c.init?.method === 'DELETE');
		expect(deletes.length).toBe(1);
		expect(deletes[0].url).toBe('http://localhost:3010/api/session-folders/parent');
	});

	test('deleteFolder rolls back the optimistic removal when the DELETE fails', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'frag', name: 'Fragile' }));
		fake.respond = () => jsonResponse({ error: 'busy' }, 409);

		useSessionFoldersStore.getState().deleteFolder('dir:/repo', 'frag');
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo']).toEqual([]);

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'].map(f => f.id)).toEqual(['frag']);
	});

	test('addSessionsToFolder appends sessions optimistically and POSTs', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'fldr', name: 'F' }));
		fake.respond = () => jsonResponse(makeFolderDto({ id: 'fldr', items: [
			{ chat_id: 'c1', sort_index: 0, added_at: '2026-01-01T00:00:00Z' },
			{ chat_id: 'c2', sort_index: 1, added_at: '2026-01-01T00:00:00Z' },
		]}));

		useSessionFoldersStore.getState().addSessionsToFolder('dir:/repo', 'fldr', ['c1', 'c2']);
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].sessionIds).toEqual(['c1', 'c2']);

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		const post = fake.calls.find(c => c.init?.method === 'POST');
		expect(post?.url).toBe('http://localhost:3010/api/session-folders/fldr/items');
		expect(JSON.parse(post!.init!.body as string)).toEqual({ chat_ids: ['c1', 'c2'] });
	});

	test('addSessionToFolder is sugar for addSessionsToFolder with a single id', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'sg' }));
		fake.respond = () => jsonResponse(makeFolderDto({ id: 'sg' }));

		useSessionFoldersStore.getState().addSessionToFolder('dir:/repo', 'sg', 'cx');
		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		const post = fake.calls.find(c => c.init?.method === 'POST');
		expect(JSON.parse(post!.init!.body as string)).toEqual({ chat_ids: ['cx'] });
	});

	test('removeSessionFromFolder removes locally and DELETEs', async () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({
			id: 'rf', items: [{ chat_id: 'c1', sort_index: 0, added_at: '2026-01-01T00:00:00Z' }],
		}));
		fake.respond = () => new Response(null, { status: 204 });

		useSessionFoldersStore.getState().removeSessionFromFolder('dir:/repo', 'c1');
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].sessionIds).toEqual([]);

		await flushMicrotasks();
		await new Promise(r => setTimeout(r, 5));
		const del = fake.calls.find(c => c.init?.method === 'DELETE');
		expect(del?.url).toBe('http://localhost:3010/api/session-folders/rf/items/c1');
	});

	test('toggleFolderCollapse flips collapse state without hitting the network', () => {
		useSessionFoldersStore.getState().toggleFolderCollapse('xyz');
		expect(useSessionFoldersStore.getState().collapsedFolderIds.has('xyz')).toBe(true);
		useSessionFoldersStore.getState().toggleFolderCollapse('xyz');
		expect(useSessionFoldersStore.getState().collapsedFolderIds.has('xyz')).toBe(false);
		expect(fake.calls.length).toBe(0);
	});

	test('cleanupSessions removes stale session ids locally (no network)', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({
			id: 'cl', items: [
				{ chat_id: 'live',  sort_index: 0, added_at: '2026-01-01T00:00:00Z' },
				{ chat_id: 'stale', sort_index: 1, added_at: '2026-01-01T00:00:00Z' },
			],
		}));

		useSessionFoldersStore.getState().cleanupSessions('dir:/repo', new Set(['live']));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].sessionIds).toEqual(['live']);
		expect(fake.calls.length).toBe(0);
	});

	test('cleanupSessions prunes empty folders only under __archived__ scopes', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({
			id: 'arc1', scope_key: '__archived__:/repo',
			items: [{ chat_id: 'gone', sort_index: 0, added_at: '2026-01-01T00:00:00Z' }],
		}));
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({
			id: 'live1', scope_key: 'dir:/repo',
			items: [{ chat_id: 'gone', sort_index: 0, added_at: '2026-01-01T00:00:00Z' }],
		}));

		useSessionFoldersStore.getState().cleanupSessions('__archived__:/repo', new Set());
		useSessionFoldersStore.getState().cleanupSessions('dir:/repo', new Set());

		// Archived scope prunes the now-empty folder.
		expect(useSessionFoldersStore.getState().foldersMap['__archived__:/repo']).toEqual([]);
		// Live scope keeps the empty folder.
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'].length).toBe(1);
	});

	test('getSessionFolderId locates the folder containing a given session', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({
			id: 'g1', items: [{ chat_id: 'cx', sort_index: 0, added_at: '2026-01-01T00:00:00Z' }],
		}));
		expect(useSessionFoldersStore.getState().getSessionFolderId('dir:/repo', 'cx')).toBe('g1');
		expect(useSessionFoldersStore.getState().getSessionFolderId('dir:/repo', 'unknown')).toBeNull();
	});

	test('SSE folder_created appends via installSessionFoldersSync', () => {
		const unsubscribe = installSessionFoldersSync();
		FakeEventSource.last!.fire('folder_created', JSON.stringify({ folder: makeFolderDto({ id: 'sse-c' }) }));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'].map(f => f.id)).toContain('sse-c');
		unsubscribe();
	});

	test('SSE folder_updated replaces the existing folder', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'sse-u', name: 'Old' }));
		const unsubscribe = installSessionFoldersSync();
		FakeEventSource.last!.fire('folder_updated', JSON.stringify({ folder: makeFolderDto({ id: 'sse-u', name: 'New' }) }));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'][0].name).toBe('New');
		unsubscribe();
	});

	test('SSE folder_deleted removes from local state', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'sse-d' }));
		const unsubscribe = installSessionFoldersSync();
		FakeEventSource.last!.fire('folder_deleted', JSON.stringify({ folder_id: 'sse-d' }));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo']).toEqual([]);
		unsubscribe();
	});

	test('SSE folder_created for an already-known id is idempotent', () => {
		useSessionFoldersStore.getState()._applyFolderCreated(makeFolderDto({ id: 'dup' }));
		const unsubscribe = installSessionFoldersSync();
		FakeEventSource.last!.fire('folder_created', JSON.stringify({ folder: makeFolderDto({ id: 'dup' }) }));
		expect(useSessionFoldersStore.getState().foldersMap['dir:/repo'].length).toBe(1);
		unsubscribe();
	});
});
