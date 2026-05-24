import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	createWorktree,
	listProjectWorktrees,
	removeProjectWorktree,
	validateWorktreeCreate,
} from './worktreeManager';

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

class FakeFetch {
	calls: { url: string; init?: RequestInit }[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response =
		() => new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } });

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

describe('worktreeManager', () => {
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

	test('listProjectWorktrees: empty path → empty array, no fetch', async () => {
		expect(await listProjectWorktrees('')).toEqual([]);
		expect(fake.calls.length).toBe(0);
	});

	test('listProjectWorktrees: passes project_path query and returns the list', async () => {
		fake.respond = () => json([{ path: '/repo/.worktrees/feat-x', branch: 'feat/x' }]);
		const list = await listProjectWorktrees({ id: 'p1', path: '/repo' });
		expect(list).toEqual([{ path: '/repo/.worktrees/feat-x', branch: 'feat/x' }]);
		expect(fake.calls[0].url).toContain('project_path=%2Frepo');
	});

	test('listProjectWorktrees: returns empty on backend failure', async () => {
		fake.respond = () => json({ error: 'busy' }, 500);
		expect(await listProjectWorktrees('/repo')).toEqual([]);
	});

	test('createWorktree: missing args → returns null without fetch', async () => {
		expect(await createWorktree('', '/p/wt', 'feat/x')).toBeNull();
		expect(await createWorktree('/p', '', 'feat/x')).toBeNull();
		expect(await createWorktree('/p', '/p/wt', '')).toBeNull();
		expect(fake.calls.length).toBe(0);
	});

	test('createWorktree: POSTs and returns the created metadata', async () => {
		fake.respond = () => json({ path: '/repo/.worktrees/feat-x', branch: 'feat/x', head: 'abc' });
		const meta = await createWorktree('/repo', '/repo/.worktrees/feat-x', 'feat/x');
		expect(meta).toEqual({ path: '/repo/.worktrees/feat-x', branch: 'feat/x', head: 'abc' });
	});

	test('createWorktree: returns null on backend rejection', async () => {
		fake.respond = () => json({ error: 'branch exists' }, 409);
		expect(await createWorktree('/repo', '/repo/.worktrees/feat-x', 'feat/x')).toBeNull();
	});

	test('removeProjectWorktree: 204 → true', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		expect(await removeProjectWorktree('/repo', '/repo/.worktrees/feat-x')).toBe(true);
	});

	test('removeProjectWorktree: backend failure → false', async () => {
		fake.respond = () => json({ error: 'dirty' }, 409);
		expect(await removeProjectWorktree('/repo', '/repo/.worktrees/feat-x')).toBe(false);
	});

	test('removeProjectWorktree: empty args → false without fetch', async () => {
		expect(await removeProjectWorktree('', '/wt')).toBe(false);
		expect(await removeProjectWorktree('/p', '')).toBe(false);
		expect(fake.calls.length).toBe(0);
	});

	// ── validateWorktreeCreate (pure) ─────────────────────────────

	test('validateWorktreeCreate rejects empty branchName', () => {
		expect(validateWorktreeCreate({ branchName: '' })).toEqual({ ok: false, reason: 'Branch name is required' });
		expect(validateWorktreeCreate({ branchName: '   ' })).toEqual({ ok: false, reason: 'Branch name is required' });
	});

	test('validateWorktreeCreate rejects illegal chars', () => {
		expect(validateWorktreeCreate({ branchName: 'has spaces' })).toMatchObject({ ok: false });
		expect(validateWorktreeCreate({ branchName: 'has..dots' })).toMatchObject({ ok: false });
		expect(validateWorktreeCreate({ branchName: '-leading-dash' })).toMatchObject({ ok: false });
	});

	test('validateWorktreeCreate accepts a clean branch name', () => {
		expect(validateWorktreeCreate({ branchName: 'feature/abc-123' })).toEqual({ ok: true });
	});

	test('validateWorktreeCreate rejects empty worktreeName when provided', () => {
		expect(validateWorktreeCreate({ branchName: 'ok', worktreeName: '   ' })).toMatchObject({ ok: false });
	});
});
