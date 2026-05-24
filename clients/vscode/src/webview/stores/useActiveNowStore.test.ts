import { beforeEach, describe, expect, test } from 'bun:test';

import type { Session } from '@opencode-ai/sdk/v2';
import { useActiveNowStore } from './useActiveNowStore';

function session(id: string, over: Partial<Session> = {}): Session {
	return {
		id,
		time: { created: Date.now(), updated: Date.now() },
		...over,
	};
}

describe('useActiveNowStore', () => {
	beforeEach(() => {
		useActiveNowStore.setState({ entries: [] });
	});

	test('addSession prepends new ids in MRU order', () => {
		useActiveNowStore.getState().addSession('a');
		useActiveNowStore.getState().addSession('b');
		expect(useActiveNowStore.getState().entries.map(e => e.sessionId)).toEqual(['b', 'a']);
	});

	test('addSession ignores empty id', () => {
		useActiveNowStore.getState().addSession('');
		expect(useActiveNowStore.getState().entries).toEqual([]);
	});

	test('addSession dedupes — re-adding an existing id is a no-op (same identity)', () => {
		useActiveNowStore.getState().addSession('a');
		const before = useActiveNowStore.getState().entries;
		useActiveNowStore.getState().addSession('a');
		expect(useActiveNowStore.getState().entries).toBe(before); // identity-stable
	});

	test('setEntries replaces the list; identity equal is a no-op', () => {
		const entries = [{ sessionId: 'x' }];
		useActiveNowStore.getState().setEntries(entries);
		expect(useActiveNowStore.getState().entries).toBe(entries);
		// Re-set with same reference: identity-stable, no recompute.
		useActiveNowStore.getState().setEntries(entries);
		expect(useActiveNowStore.getState().entries).toBe(entries);
	});

	test('prune drops archived sessions but keeps unknown ids (server may catch up later)', () => {
		useActiveNowStore.setState({ entries: [
			{ sessionId: 'live' },
			{ sessionId: 'archived' },
			{ sessionId: 'unknown' },
		] });
		const map = new Map<string, Session>([
			['live',     session('live')],
			['archived', session('archived', { time: { archived: 1, updated: Date.now() } })],
		]);
		useActiveNowStore.getState().prune(map);
		expect(useActiveNowStore.getState().entries.map(e => e.sessionId)).toEqual(['live', 'unknown']);
	});

	test('prune drops entries whose updated timestamp is older than the 36h cutoff', () => {
		const now      = Date.now();
		const tooStale = now - 37 * 60 * 60 * 1000;
		useActiveNowStore.setState({ entries: [{ sessionId: 'old' }, { sessionId: 'recent' }] });
		const map = new Map<string, Session>([
			['old',    session('old',    { time: { updated: tooStale } })],
			['recent', session('recent', { time: { updated: now } })],
		]);
		useActiveNowStore.getState().prune(map);
		expect(useActiveNowStore.getState().entries.map(e => e.sessionId)).toEqual(['recent']);
	});

	test('prune is identity-stable when nothing actually changed', () => {
		useActiveNowStore.setState({ entries: [{ sessionId: 'a' }] });
		const before = useActiveNowStore.getState().entries;
		useActiveNowStore.getState().prune(new Map<string, Session>([['a', session('a')]]));
		expect(useActiveNowStore.getState().entries).toBe(before);
	});
});
