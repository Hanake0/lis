import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	applyGitStash,
	checkIsGitRepository,
	deleteGitBranch,
	generateCommitMessage,
	getCommitFileDiff,
	getGitBranches,
	getGitWorktreeBootstrapStatus,
	listGitStashes,
	renameBranch,
} from './gitApi';

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
	calls:   RecordedCall[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response =
		(_req) => new Response('null', { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

function json(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

describe('gitApi (wired surface)', () => {
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
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('checkIsGitRepository returns false on empty path', async () => {
		expect(await checkIsGitRepository('')).toBe(false);
		expect(await checkIsGitRepository('   ')).toBe(false);
	});

	test('checkIsGitRepository returns true when backend reports is_git_repo', async () => {
		fake.respond = () => json({ is_git_repo: true, branch: 'main' });
		expect(await checkIsGitRepository('/repo')).toBe(true);
	});

	test('checkIsGitRepository returns false when backend reports not a repo', async () => {
		fake.respond = () => json({ is_git_repo: false });
		expect(await checkIsGitRepository('/no-repo')).toBe(false);
	});

	test('checkIsGitRepository returns false on backend error (404 / 500)', async () => {
		fake.respond = () => json({ error: 'invalid path' }, 400);
		expect(await checkIsGitRepository('/bad')).toBe(false);
	});

	test('getGitBranches returns the backend payload', async () => {
		fake.respond = () => json({ local: ['main', 'dev'], remote: ['origin/main'], current: 'main' });
		const result = await getGitBranches('/repo');
		expect(result).toEqual({ local: ['main', 'dev'], remote: ['origin/main'], current: 'main' });
	});

	test('getGitBranches returns null on backend failure', async () => {
		fake.respond = () => json({ error: 'busy' }, 500);
		expect(await getGitBranches('/repo')).toBeNull();
	});
});

describe('gitApi (stub surface — return type-stable fallbacks)', () => {
	test('mutating operations return false', async () => {
		expect(await deleteGitBranch('/r', 'b')).toBe(false);
		expect(await renameBranch('/r', 'a', 'b')).toBe(false);
		expect(await applyGitStash('/r', 0)).toBe(false);
	});

	test('list operations return empty defaults', async () => {
		expect(await listGitStashes('/r')).toEqual([]);
	});

	test('AI commit message returns null', async () => {
		expect(await generateCommitMessage('/r')).toBeNull();
	});

	test('diff + bootstrap status return null', async () => {
		expect(await getCommitFileDiff('/r', 'abc', 'f.ts')).toBeNull();
		expect(await getGitWorktreeBootstrapStatus('/wt')).toBeNull();
	});
});
