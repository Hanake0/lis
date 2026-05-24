import { beforeEach, describe, expect, test } from 'bun:test';

import type { Session } from '@/lib/opencode-sdk-stub';
import {
	ensureGlobalSessionsLoaded,
	refreshGlobalSessions,
	resolveGlobalSessionDirectory,
	useGlobalSessionsStore,
} from './useGlobalSessionsStore';

function makeSession(over: Partial<Session> = {}): Session {
	return {
		id:    'sess-1',
		title: 'Session 1',
		time:  { created: 1000, updated: 2000 },
		...over,
	};
}

describe('useGlobalSessionsStore', () => {
	beforeEach(() => {
		useGlobalSessionsStore.setState({
			activeSessions:      [],
			archivedSessions:    [],
			sessionsByDirectory: new Map(),
			hasLoaded:           false,
			status:              'idle',
		});
	});

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

	test('loadSessions returns current snapshot when no fallback is provided', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		const result = await useGlobalSessionsStore.getState().loadSessions();
		expect(result.activeSessions.map(x => x.id)).toEqual(['a']);
		expect(useGlobalSessionsStore.getState().status).toBe('ready');
	});

	test('loadSessions merges a fallback list into the active bucket', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		await useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'b' })]);
		const ids = useGlobalSessionsStore.getState().activeSessions.map(s => s.id).sort();
		expect(ids).toEqual(['a', 'b']);
	});

	test('loadSessions de-dups concurrent calls via the inflight latch', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		const p1 = useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'b' })]);
		// Second call while the first is still inflight: should share the underlying
		// latch so neither runs the merge twice. The outer async wrappers are
		// separate Promises but they unwrap to the same shared result.
		const p2 = useGlobalSessionsStore.getState().loadSessions([makeSession({ id: 'c' })]);
		const [r1, r2] = await Promise.all([p1, p2]);
		expect(r1).toEqual(r2);
		// Only the first fallback was merged in — proves the second call short-circuited.
		expect(r1.activeSessions.map(s => s.id).sort()).toEqual(['a', 'b']);
	});

	test('ensureGlobalSessionsLoaded short-circuits when already ready', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		const result = await ensureGlobalSessionsLoaded();
		expect(result.activeSessions.map(x => x.id)).toEqual(['a']);
	});

	test('ensureGlobalSessionsLoaded triggers a load when not yet loaded', async () => {
		const result = await ensureGlobalSessionsLoaded([makeSession({ id: 'new' })]);
		expect(result.activeSessions.map(x => x.id)).toEqual(['new']);
		expect(useGlobalSessionsStore.getState().hasLoaded).toBe(true);
	});

	test('refreshGlobalSessions always invokes loadSessions', async () => {
		useGlobalSessionsStore.getState().applySnapshot([makeSession({ id: 'a' })], []);
		const result = await refreshGlobalSessions([makeSession({ id: 'b' })]);
		const ids = result.activeSessions.map(s => s.id).sort();
		expect(ids).toEqual(['a', 'b']);
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
