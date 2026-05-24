import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useProjectsStore } from './useProjectsStore';
import { useDirectoryStore } from './useDirectoryStore';

class FakeMessageSource implements MessageSource {
	addEventListener(): void { /* noop */ }
}

class FakeEventSource implements EventSourceLike {
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	constructor(_url: string) { /* noop */ }
	close(): void { /* noop */ }
	addEventListener(): void { /* noop */ }
}

function project(over: Partial<{ id: string; path: string }> = {}): unknown {
	return {
		id:                'p1',
		path:              '/repo',
		label:             'Repo',
		added_at:          '2026-01-01T00:00:00Z',
		created_at:        '2026-01-01T00:00:00Z',
		updated_at:        '2026-01-01T00:00:00Z',
		sidebar_collapsed: false,
		sort_index:        0,
		...over,
	};
}

describe('useDirectoryStore', () => {
	const originalFetch = globalThis.fetch;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		// Mute the touchProject side-effect — these tests don't care about it.
		globalThis.fetch = async () => new Response(null, { status: 204 });
		LisRpcClient.install(new FakeMessageSource(), { eventSourceFactory: (url) => new FakeEventSource(url) });

		useProjectsStore.setState({ projects: [], activeProjectId: null, loading: false, lastError: null });
		useDirectoryStore.setState({
			currentDirectory: '',
			homeDirectory:    '',
			isHomeReady:      false,
			directoryHistory: [],
			historyIndex:     -1,
		});
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('setDirectory updates currentDirectory + appends to history', () => {
		useDirectoryStore.getState().setDirectory('/a/b');
		const s = useDirectoryStore.getState();
		expect(s.currentDirectory).toBe('/a/b');
		expect(s.directoryHistory).toEqual(['/a/b']);
		expect(s.historyIndex).toBe(0);
	});

	test('setDirectory is a no-op when path is empty or unchanged', () => {
		useDirectoryStore.getState().setDirectory('/a');
		const before = useDirectoryStore.getState();
		useDirectoryStore.getState().setDirectory('');
		useDirectoryStore.getState().setDirectory('   ');
		useDirectoryStore.getState().setDirectory('/a');
		expect(useDirectoryStore.getState()).toEqual(before);
	});

	test('setDirectory(matchingProjectPath) sets that project active', () => {
		useProjectsStore.setState({ projects: [project({ id: 'proj-1', path: '/code/repo' }) as never] });
		useDirectoryStore.getState().setDirectory('/code/repo');
		expect(useProjectsStore.getState().activeProjectId).toBe('proj-1');
	});

	test('setDirectory(nonMatchingPath) leaves active project alone', () => {
		useProjectsStore.setState({ projects: [project() as never], activeProjectId: 'p1' });
		useDirectoryStore.getState().setDirectory('/unknown');
		expect(useProjectsStore.getState().activeProjectId).toBe('p1');
	});

	test('changing the active project propagates to currentDirectory', () => {
		useProjectsStore.setState({
			projects: [project({ id: 'p1', path: '/r1' }), project({ id: 'p2', path: '/r2' })] as never,
			activeProjectId: null,
		});
		useProjectsStore.getState().setActiveProject('p2');
		expect(useDirectoryStore.getState().currentDirectory).toBe('/r2');
	});

	test('clearing active project clears currentDirectory', () => {
		useProjectsStore.setState({ projects: [project() as never], activeProjectId: 'p1' });
		useDirectoryStore.setState({ currentDirectory: '/repo' });
		useProjectsStore.getState().setActiveProject(null);
		expect(useDirectoryStore.getState().currentDirectory).toBe('');
	});

	test('synchronizeHomeDirectory flips isHomeReady and stores the path', () => {
		useDirectoryStore.getState().synchronizeHomeDirectory('/Users/octo');
		const s = useDirectoryStore.getState();
		expect(s.homeDirectory).toBe('/Users/octo');
		expect(s.isHomeReady).toBe(true);
	});

	test('synchronizeHomeDirectory is a no-op on empty or unchanged values', () => {
		useDirectoryStore.getState().synchronizeHomeDirectory('/Users/octo');
		const before = useDirectoryStore.getState();
		useDirectoryStore.getState().synchronizeHomeDirectory('');
		useDirectoryStore.getState().synchronizeHomeDirectory('   ');
		useDirectoryStore.getState().synchronizeHomeDirectory('/Users/octo');
		expect(useDirectoryStore.getState()).toEqual(before);
	});
});
