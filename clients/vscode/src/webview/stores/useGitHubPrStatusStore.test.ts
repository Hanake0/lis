import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	getGitHubPrStatusKey,
	resetPrVisualSummaryCacheForTesting,
	selectPrVisualSummaries,
	useGitHubPrStatusStore,
} from './useGitHubPrStatusStore';

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
		new Response(JSON.stringify({ results: {} }), { status: 200, headers: { 'content-type': 'application/json' } });

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

async function flushAsyncMicrotasks(): Promise<void> {
	for (let i = 0; i < 6; i++) await Promise.resolve();
	await new Promise(r => setTimeout(r, 5));
}

function resetStore(): void {
	useGitHubPrStatusStore.setState({ entries: {}, activeRequestCount: 0, totalRequestCount: 0 });
}

describe('useGitHubPrStatusStore', () => {
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
		resetStore();
	});
	afterEach(() => { globalThis.fetch = originalFetch; });

	test('getGitHubPrStatusKey produces the deterministic directory::branch shape', () => {
		expect(getGitHubPrStatusKey('/repo', 'main')).toBe('/repo::main');
		expect(getGitHubPrStatusKey('/repo', 'feat/x', 'origin')).toBe('/repo::feat/x');
	});

	test('ensureEntry is idempotent and creates a fresh empty entry', () => {
		const key = '/r::main';
		useGitHubPrStatusStore.getState().ensureEntry(key);
		const a = useGitHubPrStatusStore.getState().entries[key];
		useGitHubPrStatusStore.getState().ensureEntry(key);
		const b = useGitHubPrStatusStore.getState().entries[key];
		expect(a).toBe(b); // same reference
		expect(a.watchers).toBe(0);
		expect(a.status).toBeNull();
	});

	test('setParams merges directory+branch+remoteName into the entry identity', () => {
		const key = '/r::main';
		useGitHubPrStatusStore.getState().setParams(key, {
			directory: '/r', branch: 'main', remoteName: 'origin',
			canShow: true, githubAuthChecked: true, githubConnected: true,
		});
		const entry = useGitHubPrStatusStore.getState().entries[key];
		expect(entry.params?.directory).toBe('/r');
		expect(entry.identity).toEqual({ directory: '/r', branch: 'main', remoteName: 'origin' });
	});

	test('refresh issues POST /api/github/pr-status/by-directory with the right payload', async () => {
		fake.respond = () => jsonResponse({
			results: {
				'/r::main': {
					connected: true,
					repo: { owner: 'octocat', repo: 'Hello-World' },
					branch: 'main',
					pr: { number: 7, title: 'feat', body: null, url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', head_sha: 'sha', mergeable: true, mergeable_state: 'clean' },
					checks: { state: 'success', total: 3, pending: 0, success: 3, failure: 0 },
					can_merge: true,
					default_branch: 'main',
					resolved_remote_name: 'origin',
					error: null,
				},
			},
		});

		const key = '/r::main';
		useGitHubPrStatusStore.getState().setParams(key, {
			directory: '/r', branch: 'main', remoteName: 'origin',
			canShow: true, githubAuthChecked: true, githubConnected: true,
		});
		await useGitHubPrStatusStore.getState().refresh(key, { force: true });

		expect(fake.calls[0].url).toBe('http://localhost:3010/api/github/pr-status/by-directory');
		expect(fake.calls[0].init?.method).toBe('POST');
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.keys).toEqual([{ directory: '/r', branch: 'main' }]);

		const entry = useGitHubPrStatusStore.getState().entries[key];
		expect(entry.status?.pr?.number).toBe(7);
		expect(entry.resolvedRemoteName).toBe('origin');
		expect(entry.error).toBeNull();
		expect(entry.isInitialStatusResolved).toBe(true);
	});

	test('refresh short-circuits to a disconnected status when githubConnected is false', async () => {
		const key = '/r::main';
		useGitHubPrStatusStore.getState().setParams(key, {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: false,
		});

		await useGitHubPrStatusStore.getState().refresh(key, { force: true });

		expect(fake.calls.length).toBe(0); // no fetch fired
		const entry = useGitHubPrStatusStore.getState().entries[key];
		expect(entry.status).toEqual({ connected: false });
		expect(entry.isInitialStatusResolved).toBe(true);
	});

	test('refresh without canShow params is a no-op (entry remains untouched)', async () => {
		const key = '/r::main';
		useGitHubPrStatusStore.getState().setParams(key, {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: false, githubAuthChecked: false, githubConnected: null,
		});
		await useGitHubPrStatusStore.getState().refresh(key, { force: true });
		expect(fake.calls.length).toBe(0);
	});

	test('refresh surfaces REST failures via the error slot', async () => {
		fake.respond = () => jsonResponse({ error: 'rate limited' }, 429);
		const key = '/r::main';
		useGitHubPrStatusStore.getState().setParams(key, {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		});
		await useGitHubPrStatusStore.getState().refresh(key, { force: true });
		expect(useGitHubPrStatusStore.getState().entries[key].error).toContain('rate limited');
		expect(useGitHubPrStatusStore.getState().entries[key].isLoading).toBe(false);
	});

	test('refresh fans out to every entry that shares a directory::branch signature', async () => {
		fake.respond = () => jsonResponse({
			results: {
				'/r::main': {
					connected: true,
					repo: { owner: 'o', repo: 'r' },
					branch: 'main',
					pr: { number: 1, title: 'x', body: null, url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', head_sha: 's', mergeable: true, mergeable_state: 'clean' },
					checks: null,
					can_merge: true,
					default_branch: 'main',
					resolved_remote_name: 'origin',
					error: null,
				},
			},
		});
		// Two entries with different keys but the same signature
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('alpha', params);
		useGitHubPrStatusStore.getState().setParams('beta',  params);

		await useGitHubPrStatusStore.getState().refresh('alpha', { force: true });

		expect(useGitHubPrStatusStore.getState().entries['alpha'].status?.pr?.number).toBe(1);
		expect(useGitHubPrStatusStore.getState().entries['beta'].status?.pr?.number).toBe(1);
		expect(fake.calls.length).toBe(1); // single network call updates both
	});

	test('refresh respects the TTL latch (no double-fetch within PR_REVALIDATE_TTL_MS)', async () => {
		fake.respond = () => jsonResponse({ results: { '/r::main': { connected: true } } });
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);

		await useGitHubPrStatusStore.getState().refresh('k', { force: true });
		await useGitHubPrStatusStore.getState().refresh('k'); // no force → should short-circuit
		expect(fake.calls.length).toBe(1);
	});

	test('refresh { force: true } bypasses the TTL latch', async () => {
		fake.respond = () => jsonResponse({ results: { '/r::main': { connected: true } } });
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);

		await useGitHubPrStatusStore.getState().refresh('k', { force: true });
		await useGitHubPrStatusStore.getState().refresh('k', { force: true });
		expect(fake.calls.length).toBe(2);
	});

	test('refresh { onlyExistingPr } skips the fetch when no entry has a PR yet', async () => {
		fake.respond = () => jsonResponse({ results: {} });
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);

		await useGitHubPrStatusStore.getState().refresh('k', { force: true, onlyExistingPr: true });
		expect(fake.calls.length).toBe(0);
	});

	test('refresh carries the previous PR body forward when the fresh payload omits it', async () => {
		fake.respond = () => jsonResponse({
			results: {
				'/r::main': {
					connected: true,
					pr: { number: 7, title: 'first', body: 'detailed body', url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', head_sha: 's', mergeable: true, mergeable_state: 'clean' },
				},
			},
		});
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);
		await useGitHubPrStatusStore.getState().refresh('k', { force: true });
		expect(useGitHubPrStatusStore.getState().entries['k'].status?.pr?.body).toBe('detailed body');

		// Second fetch returns same PR but with body trimmed away (the list-PRs path).
		fake.respond = () => jsonResponse({
			results: {
				'/r::main': {
					connected: true,
					pr: { number: 7, title: 'second', body: '', url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', head_sha: 's', mergeable: true, mergeable_state: 'clean' },
				},
			},
		});
		await useGitHubPrStatusStore.getState().refresh('k', { force: true });
		const pr = useGitHubPrStatusStore.getState().entries['k'].status?.pr;
		expect(pr?.title).toBe('second');
		expect(pr?.body).toBe('detailed body'); // preserved
	});

	test('startWatching increments the refcount and triggers an immediate refresh', async () => {
		fake.respond = () => jsonResponse({ results: { '/r::main': { connected: true } } });
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);
		useGitHubPrStatusStore.getState().startWatching('k');
		expect(useGitHubPrStatusStore.getState().entries['k'].watchers).toBe(1);

		// Let the immediate refresh + 2 setTimeout bootstrap retries fire.
		await flushAsyncMicrotasks();
		expect(fake.calls.length).toBeGreaterThanOrEqual(1);

		useGitHubPrStatusStore.getState().stopWatching('k');
	});

	test('stopWatching decrements the refcount and releases timers on the last unwatch', () => {
		const params = {
			directory: '/r', branch: 'main', remoteName: null,
			canShow: true, githubAuthChecked: true, githubConnected: true,
		};
		useGitHubPrStatusStore.getState().setParams('k', params);
		useGitHubPrStatusStore.getState().startWatching('k');
		useGitHubPrStatusStore.getState().startWatching('k');
		expect(useGitHubPrStatusStore.getState().entries['k'].watchers).toBe(2);

		useGitHubPrStatusStore.getState().stopWatching('k');
		expect(useGitHubPrStatusStore.getState().entries['k'].watchers).toBe(1);
		useGitHubPrStatusStore.getState().stopWatching('k');
		expect(useGitHubPrStatusStore.getState().entries['k'].watchers).toBe(0);
	});

	test('refreshTargets dedups by signature and refreshes each unique target', async () => {
		fake.respond = () => jsonResponse({ results: { '/a::main': { connected: true }, '/b::main': { connected: true } } });
		const base = { remoteName: null, canShow: true, githubAuthChecked: true, githubConnected: true };
		// refreshTargets keys entries via getGitHubPrStatusKey, so consumers must
		// register entries under the same key. Two callers can register the same
		// directory+branch under that one canonical key — refreshTargets dedups.
		useGitHubPrStatusStore.getState().setParams('/a::main', { directory: '/a', branch: 'main', ...base });
		useGitHubPrStatusStore.getState().setParams('/b::main', { directory: '/b', branch: 'main', ...base });

		await useGitHubPrStatusStore.getState().refreshTargets([
			{ directory: '/a', branch: 'main' },
			{ directory: '/a', branch: 'main' }, // dup → collapses to one fetch
			{ directory: '/b', branch: 'main' },
			{ directory: '',   branch: ''   },   // skipped
		], { force: true });

		// Two unique signatures → two HTTP calls.
		expect(fake.calls.length).toBe(2);
	});

	test('updateStatus replaces the status via an updater function', () => {
		const key = '/r::main';
		useGitHubPrStatusStore.getState().ensureEntry(key);
		useGitHubPrStatusStore.getState().updateStatus(key, () => ({
			connected: true,
			repo: { owner: 'o', repo: 'r' },
			branch: 'main',
		}));
		expect(useGitHubPrStatusStore.getState().entries[key].status?.repo?.owner).toBe('o');
	});

	// ── Visual summary ─────────────────────────────────────────────

	test('selectPrVisualSummaries returns an empty Map when no entries have a PR', () => {
		resetPrVisualSummaryCacheForTesting();
		const m = selectPrVisualSummaries({}, ['/a::main']);
		expect(m.size).toBe(0);
	});

	test('selectPrVisualSummaries derives an "open" visual state for a clean merge-ready PR', () => {
		resetPrVisualSummaryCacheForTesting();
		const m = selectPrVisualSummaries({
			'/r::main': {
				status: {
					connected: true,
					pr: { number: 1, title: 't', body: null, url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: true, mergeableState: 'clean' },
					checks: { state: 'success', total: 1, pending: 0, success: 1, failure: 0 },
				},
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		}, ['/r::main']);
		expect(m.get('/r::main')?.visualState).toBe('open');
	});

	test('selectPrVisualSummaries returns "draft" when the PR is a draft', () => {
		resetPrVisualSummaryCacheForTesting();
		const m = selectPrVisualSummaries({
			'/r::main': {
				status: {
					connected: true,
					pr: { number: 1, title: 't', body: null, url: 'u', state: 'open', draft: true, base: 'main', head: 'feat', headSha: 's', mergeable: true, mergeableState: 'clean' },
				},
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		}, ['/r::main']);
		expect(m.get('/r::main')?.visualState).toBe('draft');
	});

	test('selectPrVisualSummaries returns "blocked" when checks failed or merge is dirty', () => {
		resetPrVisualSummaryCacheForTesting();
		const m = selectPrVisualSummaries({
			'/r::main': {
				status: {
					connected: true,
					pr: { number: 1, title: 't', body: null, url: 'u', state: 'open', draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: true, mergeableState: 'dirty' },
					checks: { state: 'failure', total: 2, pending: 0, success: 0, failure: 2 },
				},
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		}, ['/r::main']);
		expect(m.get('/r::main')?.visualState).toBe('blocked');
	});

	test('selectPrVisualSummaries returns "merged" / "closed" for terminal states', () => {
		resetPrVisualSummaryCacheForTesting();
		const entries = {
			'/a::main': {
				status: { connected: true, pr: { number: 1, title: 't', body: null, url: 'u', state: 'merged' as const, draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: false, mergeableState: 'clean' } },
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
			'/b::main': {
				status: { connected: true, pr: { number: 2, title: 't', body: null, url: 'u', state: 'closed' as const, draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: false, mergeableState: 'clean' } },
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		};
		const m = selectPrVisualSummaries(entries, ['/a::main', '/b::main']);
		expect(m.get('/a::main')?.visualState).toBe('merged');
		expect(m.get('/b::main')?.visualState).toBe('closed');
	});

	test('selectPrVisualSummaries returns the same Map identity when nothing changed', () => {
		resetPrVisualSummaryCacheForTesting();
		const entries = {
			'/r::main': {
				status: { connected: true, pr: { number: 1, title: 't', body: null, url: 'u', state: 'open' as const, draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: true, mergeableState: 'clean' } },
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		};
		const m1 = selectPrVisualSummaries(entries, ['/r::main']);
		const m2 = selectPrVisualSummaries(entries, ['/r::main']);
		expect(m1).toBe(m2);
	});

	test('selectPrVisualSummaries accepts a Set of keys as well as an array', () => {
		resetPrVisualSummaryCacheForTesting();
		const entries = {
			'/r::main': {
				status: { connected: true, pr: { number: 1, title: 't', body: null, url: 'u', state: 'open' as const, draft: false, base: 'main', head: 'feat', headSha: 's', mergeable: true, mergeableState: 'clean' } },
				isLoading: false, error: null, isInitialStatusResolved: true,
				lastRefreshAt: 0, lastDiscoveryPollAt: 0, watchers: 0,
				params: null, identity: null, resolvedRemoteName: null,
			},
		};
		const m = selectPrVisualSummaries(entries, new Set(['/r::main']));
		expect(m.size).toBe(1);
	});
});
