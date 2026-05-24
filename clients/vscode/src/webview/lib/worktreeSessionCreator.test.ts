import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { createWorktreeSessionForNewBranch } from './worktreeSessionCreator';

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
	calls:    RecordedCall[]  = [];
	respond:  (req: { url: string; init?: RequestInit }) => Response | Promise<Response>
		= (req) => this.defaultRespond(req);

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}

	defaultRespond(req: { url: string; init?: RequestInit }): Response {
		const { url, init } = req;
		const method = init?.method ?? 'GET';
		if (method === 'POST' && url.endsWith('/api/worktrees')) {
			const body = JSON.parse(init!.body as string);
			return json({ path: body.new_path, branch: body.branch, head: 'abc' });
		}
		if (method === 'POST' && url.endsWith('/api/chats')) {
			return json({ chat_id: 'web:created', name: 'worktree: feat-x', project_id: 'p1', created_at: 'now', updated_at: 'now' }, 201);
		}
		return new Response('', { status: 404 });
	}
}

function json(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
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

describe('createWorktreeSessionForNewBranch', () => {
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

		useProjectsStore.setState({ projects: [project() as never], activeProjectId: null });
		useSessionUIStore.setState({ worktreeMetadata: new Map() });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('returns null when projectPath or branchName is empty', async () => {
		expect(await createWorktreeSessionForNewBranch('', 'b')).toBeNull();
		expect(await createWorktreeSessionForNewBranch('/p', '')).toBeNull();
		expect(await createWorktreeSessionForNewBranch('   ', '   ')).toBeNull();
	});

	test('happy path: POST /worktrees → POST /chats → returns chat id + path', async () => {
		const result = await createWorktreeSessionForNewBranch('/repo', 'feat/x', { sessionTitle: 'fix-x' });
		expect(result?.id).toBe('web:created');
		expect(result?.path).toBe('/repo/.worktrees/feat-x');

		const wtCall = fake.calls.find(c => c.url.endsWith('/api/worktrees'))!;
		const wtBody = JSON.parse(wtCall.init!.body as string);
		expect(wtBody.project_path).toBe('/repo');
		expect(wtBody.branch).toBe('feat/x');
		expect(wtBody.new_path).toBe('/repo/.worktrees/feat-x');

		const chatCall = fake.calls.find(c => c.url.endsWith('/api/chats'))!;
		const chatBody = JSON.parse(chatCall.init!.body as string);
		expect(chatBody.name).toBe('fix-x');
		expect(chatBody.project_id).toBe('p1');
		expect(chatBody.channel).toBe('worktree');
	});

	test('default session title falls back to "worktree: <branch>"', async () => {
		await createWorktreeSessionForNewBranch('/repo', 'feat/x');
		const chatCall = fake.calls.find(c => c.url.endsWith('/api/chats'))!;
		expect(JSON.parse(chatCall.init!.body as string).name).toBe('worktree: feat/x');
	});

	test('parks worktree metadata on session-ui-store under the new chat id', async () => {
		const result = await createWorktreeSessionForNewBranch('/repo', 'feat/x');
		const meta = (useSessionUIStore.getState() as { worktreeMetadata: Map<string, { path: string; kind: string }> }).worktreeMetadata;
		expect(meta.get(result!.id)).toMatchObject({ path: '/repo/.worktrees/feat-x', kind: 'standard' });
	});

	test('sets project active when one matches', async () => {
		await createWorktreeSessionForNewBranch('/repo', 'feat/x');
		expect(useProjectsStore.getState().activeProjectId).toBe('p1');
	});

	test('no matching project: chat is bound to project_id=null', async () => {
		useProjectsStore.setState({ projects: [], activeProjectId: null });
		await createWorktreeSessionForNewBranch('/unknown', 'feat/x');
		const chatCall = fake.calls.find(c => c.url.endsWith('/api/chats'))!;
		expect(JSON.parse(chatCall.init!.body as string).project_id).toBeNull();
	});

	test('worktree failure propagates and the chat call never happens', async () => {
		fake.respond = (req) => {
			if (req.url.endsWith('/api/worktrees')) return json({ error: 'branch exists' }, 409);
			return fake.defaultRespond(req);
		};
		await expect(createWorktreeSessionForNewBranch('/repo', 'feat/x')).rejects.toThrow(/branch exists/);
		expect(fake.calls.find(c => c.url.endsWith('/api/chats'))).toBeUndefined();
	});

	test('chat failure propagates and the worktree call is not rolled back (user can recover it)', async () => {
		fake.respond = (req) => {
			if (req.url.endsWith('/api/chats')) return json({ error: 'busy' }, 500);
			return fake.defaultRespond(req);
		};
		await expect(createWorktreeSessionForNewBranch('/repo', 'feat/x')).rejects.toThrow();
		// Worktree call was issued — that's the intended behaviour (no orphan-removal here).
		expect(fake.calls.filter(c => c.url.endsWith('/api/worktrees'))).toHaveLength(1);
	});

	test('Windows path: backslash separator preserved when input has no forward slashes', async () => {
		await createWorktreeSessionForNewBranch('C:\\dev\\repo', 'feat/x');
		const wtCall = fake.calls.find(c => c.url.endsWith('/api/worktrees'))!;
		expect(JSON.parse(wtCall.init!.body as string).new_path).toBe('C:\\dev\\repo\\.worktrees\\feat-x');
	});
});
