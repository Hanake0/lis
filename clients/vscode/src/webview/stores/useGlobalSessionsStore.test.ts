import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import type { Session } from '@/lib/opencode-sdk-stub';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	chatSummaryToSession,
	ensureGlobalSessionsLoaded,
	refreshGlobalSessions,
	resolveGlobalSessionDirectory,
	useGlobalSessionsStore,
} from './useGlobalSessionsStore';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_t: 'message', h: (e: { data: unknown }) => void): void { this.handlers.push(h); }
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

class FakeEventSource implements EventSourceLike {
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	constructor(_url: string) { /* noop */ }
	close(): void { /* noop */ }
	addEventListener(): void { /* noop */ }
}

interface RecordedCall { url: string; init?: RequestInit }

class FakeFetch {
	calls: RecordedCall[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response = () =>
		new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

function jsonResponse(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function makeSession(over: Partial<Session> = {}): Session {
	return {
		id:    'sess-1',
		title: 'Session 1',
		time:  { created: 1000, updated: 2000 },
		...over,
	};
}

describe('useGlobalSessionsStore', () => {
	const originalFetch = globalThis.fetch;
	let source: FakeMessageSource;
	let fake:   FakeFetch;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		source = new FakeMessageSource();
		fake   = new FakeFetch();
		globalThis.fetch = fake.asFetch();
		LisRpcClient.install(source, { eventSourceFactory: (url) => new FakeEventSource(url) });
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
		useGlobalSessionsStore.setState({
			activeSessions:      [],
			archivedSessions:    [],
			sessionsByDirectory: new Map(),
			hasLoaded:           false,
			status:              'idle',
		});
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('applySnapshot installs active + archived lists and rebuilds the directory map', () => {
		const s1 = makeSession({ id: 'a', directory: '/repo' });
		const s2 = makeSession({ id: 'b', directory: '/other' });
		const arc = makeSession({ id: 'c', time: { created: 1, updated: 2, archived: 3 } });

		useGlobalSessionsStore.getState().applySnapshot([s1, s2], [arc]);

		const s = useGlobalSessionsStore.getState();
		expect(s.activeSessions.map(x => x.id)).toEqual(['a', 'b']);
		expect(s.archivedSessions.map(x => x.id)).toEqual(['c']);
		expect(s.hasLoaded).toBe(true);
		expect(s.status).toBe('ready');
		expect(Array.from(s.sessionsByDirectory.keys()).sort()).toEqual(['/other', '/repo']);
	});

	test('applySnapshot is a no-op when nothing changed (same identity)', () => {
		const s1 = makeSession({ id: 'a', directory: '/x' });
		useGlobalSessionsStore.getState().applySnapshot([s1], []);
		const before = useGlobalSessionsStore.getState();
		useGlobalSessionsStore.getState().applySnapshot(before.activeSessions, before.archivedSessions);
		const after = useGlobalSessionsStore.getState();
		expect(after.activeSessions).toBe(before.activeSessions);
		expect(after.archivedSessions).toBe(before.archivedSessions);
		expect(after.sessionsByDirectory).toBe(before.sessionsByDirectory);
	});

	test('upsertSession adds a new session at the head of the active list', () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		useGlobalSessionsStore.getState().upsertSession(makeSession({ id: 'b' }));
		expect(useGlobalSessionsStore.getState().activeSessions.map(s => s.id)).toEqual(['b', 'a']);
	});

	test('upsertSession replaces an existing entry in place when signature differs', () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a', title: 'Old' })], []);
		useGlobalSessionsStore.getState().upsertSession(makeSession({ id: 'a', title: 'New', time: { created: 1000, updated: 9999 } }));
		const list = useGlobalSessionsStore.getState().activeSessions;
		expect(list.length).toBe(1);
		expect(list[0].title).toBe('New');
	});

	test('upsertSession with identical signature is a no-op (preserves reference)', () => {
		const original = makeSession({ id: 'a', title: 'Same' });
		useGlobalSessionsStore.getState().applySnapshot([original], []);
		const beforeList = useGlobalSessionsStore.getState().activeSessions;
		useGlobalSessionsStore.getState().upsertSession({ ...original });
		expect(useGlobalSessionsStore.getState().activeSessions).toBe(beforeList);
	});

	test('upsertSession with archived flag moves it from active to archived', () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		useGlobalSessionsStore.getState().upsertSession(makeSession({ id: 'a', time: { created: 1, updated: 2, archived: 3 } }));
		const s = useGlobalSessionsStore.getState();
		expect(s.activeSessions.length).toBe(0);
		expect(s.archivedSessions.map(x => x.id)).toEqual(['a']);
	});

	test('removeSessions drops ids from both buckets', () => {
		useGlobalSessionsStore.getState().applySnapshot(
			[makeSession({ id: 'a' }), makeSession({ id: 'b' })],
			[makeSession({ id: 'c', time: { archived: 1 } })],
		);
		useGlobalSessionsStore.getState().removeSessions(['a', 'c']);
		const s = useGlobalSessionsStore.getState();
		expect(s.activeSessions.map(x => x.id)).toEqual(['b']);
		expect(s.archivedSessions).toEqual([]);
	});

	test('removeSessions with empty iterable is a no-op', () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		const before = useGlobalSessionsStore.getState().activeSessions;
		useGlobalSessionsStore.getState().removeSessions([]);
		expect(useGlobalSessionsStore.getState().activeSessions).toBe(before);
	});

	test('archiveSessions moves sessions to archived with the supplied timestamp', () => {
		useGlobalSessionsStore.getState().applySnapshot(
			[makeSession({ id: 'a' }), makeSession({ id: 'b' })],
			[],
		);
		useGlobalSessionsStore.getState().archiveSessions(['a'], 9999);
		const s = useGlobalSessionsStore.getState();
		expect(s.activeSessions.map(x => x.id)).toEqual(['b']);
		expect(s.archivedSessions[0].id).toBe('a');
		expect(s.archivedSessions[0].time?.archived).toBe(9999);
	});

	test('archiveSessions de-duplicates against an existing archived entry', () => {
		useGlobalSessionsStore.getState().applySnapshot(
			[makeSession({ id: 'a' })],
			[makeSession({ id: 'a', time: { archived: 1 } })],
		);
		useGlobalSessionsStore.getState().archiveSessions(['a'], 2);
		const s = useGlobalSessionsStore.getState();
		// Only one 'a' entry, with the new archived timestamp.
		expect(s.archivedSessions.filter(x => x.id === 'a').length).toBe(1);
		expect(s.archivedSessions[0].time?.archived).toBe(2);
	});

	test('loadSessions GETs /api/chats and buckets server rows by archived_at', async () => {
		fake.respond = () => jsonResponse([
			{ chat_id: 'a', name: 'A', project_id: null, pinned_at: null, archived_at: null,                       created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-03T00:00:00Z' },
			{ chat_id: 'b', name: 'B', project_id: null, pinned_at: null, archived_at: '2026-01-02T00:00:00Z',     created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-02T00:00:00Z' },
		]);
		const result = await useGlobalSessionsStore.getState().loadSessions();
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/chats');
		expect(result.activeSessions.map(s => s.id)).toEqual(['a']);
		expect(result.archivedSessions.map(s => s.id)).toEqual(['b']);
		expect(useGlobalSessionsStore.getState().status).toBe('ready');
	});

	test('loadSessions merges fallback ON TOP of the server rows (fallback overrides, server preserves order)', async () => {
		fake.respond = () => jsonResponse([
			{ chat_id: 'a', name: 'A',         project_id: null, pinned_at: null, archived_at: null, created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-03T00:00:00Z' },
			{ chat_id: 'b', name: 'B-server',  project_id: null, pinned_at: null, archived_at: null, created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-02T00:00:00Z' },
		]);
		const fallback = [
			makeSession({ id: 'b', title: 'B-fresher' }),  // overrides server's B
			makeSession({ id: 'c', title: 'C-local'   }),  // appended (server doesn't have it)
		];
		const result = await useGlobalSessionsStore.getState().loadSessions(fallback);
		expect(result.activeSessions.map(s => s.id)).toEqual(['a', 'b', 'c']);
		expect(result.activeSessions.find(s => s.id === 'b')?.title).toBe('B-fresher');
	});

	test('loadSessions falls back to the in-memory snapshot when the server fails, marking status=error', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		fake.respond = () => jsonResponse({ error: 'kaboom' }, 500);
		const result = await useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'b' })]);
		expect(result.activeSessions.map(s => s.id).sort()).toEqual(['a', 'b']);
		expect(useGlobalSessionsStore.getState().status).toBe('error');
	});

	test('loadSessions de-dups concurrent calls via the inflight latch', async () => {
		let resolveFetch!: (r: Response) => void;
		fake.respond = () => new Promise<Response>(r => { resolveFetch = r; });

		const p1 = useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'b' })]);
		// Second call while the first is still inflight — should share the underlying
		// latch so the second invocation does NOT make its own /api/chats request.
		const p2 = useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'c' })]);
		resolveFetch(jsonResponse([
			{ chat_id: 'a', name: 'A', project_id: null, pinned_at: null, archived_at: null, created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z' },
		]));
		const [r1, r2] = await Promise.all([p1, p2]);
		expect(r1).toEqual(r2);
		// Only the FIRST fallback ('b') was merged in — the second call short-circuited.
		expect(r1.activeSessions.map(s => s.id).sort()).toEqual(['a', 'b']);
		expect(fake.calls.length).toBe(1);
	});

	test('ensureGlobalSessionsLoaded short-circuits when already ready (no network hit)', async () => {
		useGlobalSessionsStore.setState({ hasLoaded: true, status: 'ready', activeSessions: [makeSession({ id: 'a' })] });
		const result = await ensureGlobalSessionsLoaded();
		expect(result.activeSessions.map(x => x.id)).toEqual(['a']);
		expect(fake.calls.length).toBe(0);
	});

	test('ensureGlobalSessionsLoaded triggers a load when not yet loaded', async () => {
		fake.respond = () => jsonResponse([
			{ chat_id: 'srv', name: 'Server Row', project_id: null, pinned_at: null, archived_at: null, created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z' },
		]);
		const result = await ensureGlobalSessionsLoaded([makeSession({ id: 'fb' })]);
		expect(result.activeSessions.map(x => x.id).sort()).toEqual(['fb', 'srv']);
		expect(useGlobalSessionsStore.getState().hasLoaded).toBe(true);
	});

	test('refreshGlobalSessions always invokes loadSessions (re-fetches even when already ready)', async () => {
		useGlobalSessionsStore.setState({ hasLoaded: true, status: 'ready', activeSessions: [makeSession({ id: 'stale' })] });
		fake.respond = () => jsonResponse([
			{ chat_id: 'fresh', name: null, project_id: null, pinned_at: null, archived_at: null, created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z' },
		]);
		const result = await refreshGlobalSessions();
		expect(result.activeSessions.map(s => s.id)).toEqual(['fresh']);
		expect(fake.calls.length).toBe(1);
	});

	// ── chatSummaryToSession ──────────────────────────────────────

	test('chatSummaryToSession maps the wire row to the OC Session shape with epoch-ms times', () => {
		const sess = chatSummaryToSession({
			chat_id:     'c1',
			name:        'Chat One',
			project_id:  null,
			pinned_at:   null,
			archived_at: '2026-01-05T00:00:00Z',
			created_at:  '2026-01-01T00:00:00Z',
			updated_at:  '2026-01-04T00:00:00Z',
		});
		expect(sess.id).toBe('c1');
		expect(sess.title).toBe('Chat One');
		expect(sess.directory).toBeNull();
		expect(sess.time?.created).toBe(Date.UTC(2026, 0, 1));
		expect(sess.time?.updated).toBe(Date.UTC(2026, 0, 4));
		expect(sess.time?.archived).toBe(Date.UTC(2026, 0, 5));
	});

	test('chatSummaryToSession leaves title undefined when name is null', () => {
		const sess = chatSummaryToSession({
			chat_id: 'c2', name: null, project_id: null, pinned_at: null, archived_at: null,
			created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z',
		});
		expect(sess.title).toBeUndefined();
	});

	test('resolveGlobalSessionDirectory falls back through directory then project.worktree', () => {
		expect(resolveGlobalSessionDirectory(makeSession({ directory: '/x' }))).toBe('/x');
		expect(resolveGlobalSessionDirectory({
			id: 'p',
			project: { worktree: '/wt' },
		} as Session)).toBe('/wt');
		expect(resolveGlobalSessionDirectory(makeSession())).toBeNull();
	});

	test('resolveGlobalSessionDirectory normalises backslashes and trailing slashes', () => {
		expect(resolveGlobalSessionDirectory(makeSession({ directory: 'C:\\repo\\' }))).toBe('C:/repo');
		expect(resolveGlobalSessionDirectory(makeSession({ directory: '/' }))).toBe('/');
	});
});
