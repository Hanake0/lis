import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useGitStore } from './useGitStore';

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
		new Response('null', { status: 200, headers: { 'content-type': 'application/json' } });

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

describe('useGitStore', () => {
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
		useGitStore.setState({ directories: new Map() });
	});
	afterEach(() => { globalThis.fetch = originalFetch; });

	test('fetchStatus GETs /api/git/status?path= and stores branch + dirty flag', async () => {
		fake.respond = () => jsonResponse({
			is_git_repo: true, branch: 'main', ahead: 0, behind: 2, dirty: false,
		});
		await useGitStore.getState().fetchStatus('/repo');
		const dir = useGitStore.getState().directories.get('/repo');
		expect(dir?.isGitRepo).toBe(true);
		expect(dir?.status?.current).toBe('main');
		expect(dir?.status?.behind).toBe(2);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/git/status?path=%2Frepo');
	});

	test('fetchStatus on a non-repo path stores isGitRepo=false and null status', async () => {
		fake.respond = () => jsonResponse({ is_git_repo: false });
		await useGitStore.getState().fetchStatus('/no-repo');
		const dir = useGitStore.getState().directories.get('/no-repo');
		expect(dir?.isGitRepo).toBe(false);
		expect(dir?.status).toBeNull();
	});

	test('fetchStatus is cached for STATUS_TTL_MS unless force is set', async () => {
		fake.respond = () => jsonResponse({ is_git_repo: true, branch: 'main' });
		await useGitStore.getState().fetchStatus('/r');
		expect(fake.calls.length).toBe(1);
		await useGitStore.getState().fetchStatus('/r');
		expect(fake.calls.length).toBe(1); // cache hit

		await useGitStore.getState().fetchStatus('/r', { force: true });
		expect(fake.calls.length).toBe(2);
	});

	test('fetchStatus marks isGitRepo=false when the backend returns 404', async () => {
		fake.respond = () => jsonResponse({ error: 'not a repo' }, 404);
		await useGitStore.getState().fetchStatus('/x');
		const dir = useGitStore.getState().directories.get('/x');
		expect(dir?.isGitRepo).toBe(false);
		expect(dir?.isLoadingStatus).toBe(false);
	});

	test('fetchBranches stores local + remote lists', async () => {
		fake.respond = () => jsonResponse({ local: ['main', 'dev'], remote: ['origin/main'], current: 'main' });
		await useGitStore.getState().fetchBranches('/repo');
		const dir = useGitStore.getState().directories.get('/repo');
		expect(dir?.branches).toEqual(['main', 'dev', 'origin/main']);
		expect(dir?.allBranches).toEqual({ local: ['main', 'dev'], remote: ['origin/main'] });
	});

	test('ensureStatus skips when a recent status entry exists', async () => {
		fake.respond = () => jsonResponse({ is_git_repo: true, branch: 'main' });
		await useGitStore.getState().fetchStatus('/r');
		expect(fake.calls.length).toBe(1);
		await useGitStore.getState().ensureStatus('/r');
		expect(fake.calls.length).toBe(1);
	});

	test('ensureStatus triggers a fetch when no entry exists', async () => {
		fake.respond = () => jsonResponse({ is_git_repo: true, branch: 'main' });
		await useGitStore.getState().ensureStatus('/r-new');
		expect(fake.calls.length).toBe(1);
	});

	test('ensureAll fires status + branches in parallel', async () => {
		fake.respond = ({ url }) => {
			if (url.includes('/git/status'))   return jsonResponse({ is_git_repo: true, branch: 'main' });
			if (url.includes('/git/branches')) return jsonResponse({ local: ['main'], remote: [], current: 'main' });
			return jsonResponse(null);
		};
		await useGitStore.getState().ensureAll('/repo');
		expect(fake.calls.length).toBe(2);
		const dir = useGitStore.getState().directories.get('/repo');
		expect(dir?.status?.current).toBe('main');
		expect(dir?.branches).toEqual(['main']);
	});

	test('no-op surface (fetchLog/identity/prefetchDiffs/etc.) does not throw', async () => {
		const s = useGitStore.getState();
		await s.fetchLog('/r');
		await s.fetchIdentity('/r');
		s.prefetchDiffs();
		s.setLogMaxCount(100);
		s.moveStatusPathsOptimistically();
		s.restoreStatus();
		s.bumpIndexRevision();
		s.setDiff();
		s.setActiveDirectory('/r');
		s.setActiveDirectory(null);
		// No assertions; passing here means no throw.
		expect(true).toBe(true);
	});

	test('fetchStatus on empty directory is a no-op', async () => {
		await useGitStore.getState().fetchStatus('');
		expect(fake.calls.length).toBe(0);
	});

	test('fetchBranches survives a backend failure (silent — keeps prior data)', async () => {
		fake.respond = () => jsonResponse({ local: ['main'], remote: [], current: 'main' });
		await useGitStore.getState().fetchBranches('/r');
		expect(useGitStore.getState().directories.get('/r')?.branches).toEqual(['main']);

		fake.respond = () => jsonResponse({ error: 'boom' }, 500);
		await useGitStore.getState().fetchBranches('/r');
		expect(useGitStore.getState().directories.get('/r')?.branches).toEqual(['main']);
		expect(useGitStore.getState().directories.get('/r')?.isLoadingBranches).toBe(false);
	});
});
