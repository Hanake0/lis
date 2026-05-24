import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import type { CreateMultiRunParams } from '@/types/multirun';
import { useMultiRunStore } from './useMultiRunStore';

/**
 * Tests for the multi-run orchestrator. Drives the real store against a fake
 * fetch that returns scripted responses for each backend call in sequence:
 *   - GET    /api/git/status?path=…    → repo-status probe
 *   - POST   /api/worktrees            → per-model worktree
 *   - POST   /api/chats                → per-model chat
 *   - POST   /api/multi-run            → per-group fan-out
 *
 * The store stitches those into the right order. We assert against the URL
 * + method + body shape so a regression in the orchestration is caught.
 */

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
	calls:    RecordedCall[]                                                = [];
	chatSeq:  number                                                        = 0;
	/** Override per test to script per-request responses. */
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

		if (url.includes('/api/git/status'))                  return json({ isGitRepo: true, branch: 'main' });
		if (method === 'POST' && url.endsWith('/api/worktrees')) {
			const body = JSON.parse(init!.body as string);
			return json({ path: body.new_path, branch: body.branch, head: 'abcdef' });
		}
		if (method === 'POST' && url.endsWith('/api/chats')) {
			const body  = JSON.parse(init!.body as string) as { chat_id?: string };
			const id    = body.chat_id ?? `web:auto-${++this.chatSeq}`;
			return json({ chat_id: id, name: 'x', project_id: null, created_at: 'now', updated_at: 'now' }, 201);
		}
		if (method === 'POST' && url.endsWith('/api/multi-run')) return json({ run_id: 'r1' }, 202);
		return new Response('', { status: 404 });
	}

	callsTo(predicate: (c: RecordedCall) => boolean): RecordedCall[] { return this.calls.filter(predicate); }
}

function json(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function makeParams(over: Partial<CreateMultiRunParams> = {}): CreateMultiRunParams {
	return {
		name:   'Repro tests',
		groups: [
			{
				prompt: 'Diagnose the flaky test',
				models: [
					{ providerID: 'anthropic', modelID: 'claude-opus-4' },
					{ providerID: 'openai',    modelID: 'gpt-5' },
				],
			},
		],
		agent: 'investigator',
		...over,
	};
}

function makeProject(over: Partial<{ id: string; path: string }> = {}): unknown {
	return {
		id:                'proj-1',
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

describe('useMultiRunStore', () => {
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

		useMultiRunStore.setState({ isLoading: false, error: null });
		useProjectsStore.setState({ projects: [], activeProjectId: null, loading: false, lastError: null });
		useSessionUIStore.setState({ worktreeMetadata: new Map() });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	// ── Validation ────────────────────────────────────────────────

	test('rejects an empty group name', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({ name: '   ' }));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('Group name is required');
		expect(fake.calls).toEqual([]);
	});

	test('rejects when groups array is empty', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({ groups: [] }));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('At least one run group');
	});

	test('rejects a group with an empty prompt', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{ prompt: '   ', models: [{ providerID: 'a', modelID: 'm' }] }],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('prompt is required');
	});

	test('rejects a group with zero models', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{ prompt: 'p', models: [] }],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('at least 1 model');
	});

	test('rejects a group with more than 5 models', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{
				prompt: 'p',
				models: Array.from({ length: 6 }, (_, i) => ({ providerID: 'a', modelID: `m${i}` })),
			}],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('maximum 5 models');
	});

	// ── Happy path ────────────────────────────────────────────────

	test('no project: skips git probe + worktrees, mints chats unbound, fans out one prompt', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams());

		expect(fake.callsTo(c => c.url.includes('/git/status'))).toEqual([]);
		expect(fake.callsTo(c => c.url.endsWith('/worktrees'))).toEqual([]);

		const chatCalls = fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST');
		expect(chatCalls.length).toBe(2);
		for (const call of chatCalls) {
			const body = JSON.parse(call.init!.body as string);
			expect(body.project_id).toBeNull();
			expect(body.channel).toBe('multi-run');
		}

		const mrCalls = fake.callsTo(c => c.url.endsWith('/multi-run') && c.init?.method === 'POST');
		expect(mrCalls.length).toBe(1);
		const mrBody = JSON.parse(mrCalls[0].init!.body as string);
		expect(mrBody.prompt).toBe('Diagnose the flaky test');
		expect(mrBody.targets).toEqual([
			{ chat_id: 'web:auto-1', agent_name: 'investigator', model: 'anthropic/claude-opus-4' },
			{ chat_id: 'web:auto-2', agent_name: 'investigator', model: 'openai/gpt-5' },
		]);

		expect(result).toEqual({ groupSlug: 'repro-tests', sessionIds: ['web:auto-1', 'web:auto-2'], firstSessionId: 'web:auto-1' });
		expect(useMultiRunStore.getState().isLoading).toBe(false);
		expect(useMultiRunStore.getState().error).toBeNull();
	});

	test('project + git: probes status, creates worktree + chat per model, parks worktree metadata', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });

		await useMultiRunStore.getState().createMultiRun(makeParams());

		// 1. Probe.
		const probe = fake.callsTo(c => c.url.includes('/git/status'));
		expect(probe.length).toBe(1);
		expect(probe[0].url).toContain('path=%2Frepo');

		// 2. Two worktrees.
		const worktreeCalls = fake.callsTo(c => c.url.endsWith('/worktrees') && c.init?.method === 'POST');
		expect(worktreeCalls.length).toBe(2);
		const wt0 = JSON.parse(worktreeCalls[0].init!.body as string);
		expect(wt0.project_path).toBe('/repo');
		expect(wt0.branch).toBe('repro-tests/anthropic-claude-opus-4');
		expect(wt0.new_path).toBe('/repo/.worktrees/repro-tests-anthropic-claude-opus-4');

		// 3. Two chats, bound to project.
		const chatCalls = fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST');
		expect(chatCalls.length).toBe(2);
		for (const call of chatCalls) {
			const body = JSON.parse(call.init!.body as string);
			expect(body.project_id).toBe('proj-1');
		}

		// 4. Worktree metadata parked on the chats.
		const meta = (useSessionUIStore.getState() as { worktreeMetadata: Map<string, { path: string; kind: string }> }).worktreeMetadata;
		expect(meta.size).toBe(2);
		for (const value of meta.values()) {
			expect(value.kind).toBe('standard');
			expect(value.path).toContain('/repo/.worktrees/');
		}

		// 5. One multi-run fan-out.
		expect(fake.callsTo(c => c.url.endsWith('/multi-run')).length).toBe(1);
	});

	test('project not a git repo: skips worktrees, still binds chats to project', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });
		fake.respond = (req) => {
			if (req.url.includes('/git/status')) return json({ isGitRepo: false });
			return fake.defaultRespond(req);
		};

		await useMultiRunStore.getState().createMultiRun(makeParams());

		expect(fake.callsTo(c => c.url.endsWith('/worktrees'))).toEqual([]);
		const chats = fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST');
		expect(chats.length).toBe(2);
		expect(JSON.parse(chats[0].init!.body as string).project_id).toBe('proj-1');
	});

	test('isolateRuns=false bypasses worktrees even on git repos', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });

		await useMultiRunStore.getState().createMultiRun(makeParams({ isolateRuns: false }));

		expect(fake.callsTo(c => c.url.includes('/git/status'))).toEqual([]);
		expect(fake.callsTo(c => c.url.endsWith('/worktrees'))).toEqual([]);
		expect(fake.callsTo(c => c.url.endsWith('/chats')).length).toBe(2);
	});

	test('multi-group: one POST to /multi-run per group, prompts kept distinct, single worktree probe', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });
		fake.respond = (req) => {
			if (req.url.includes('/git/status')) return json({ isGitRepo: false }); // skip worktrees, keep test focused
			return fake.defaultRespond(req);
		};

		await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [
				{ prompt: 'P1', models: [{ providerID: 'a', modelID: 'm' }] },
				{ prompt: 'P2', models: [{ providerID: 'b', modelID: 'm' }] },
			],
		}));

		// Only one git/status probe regardless of group count.
		expect(fake.callsTo(c => c.url.includes('/git/status')).length).toBe(1);

		const mrCalls = fake.callsTo(c => c.url.endsWith('/multi-run'));
		expect(mrCalls.length).toBe(2);
		expect(JSON.parse(mrCalls[0].init!.body as string).prompt).toBe('P1');
		expect(JSON.parse(mrCalls[1].init!.body as string).prompt).toBe('P2');
	});

	test('duplicate model within a group: per-occurrence index suffixes worktree + chat name', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });

		await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{
				prompt: 'p',
				models: [
					{ providerID: 'a', modelID: 'm' },
					{ providerID: 'a', modelID: 'm' },
					{ providerID: 'b', modelID: 'm' },
				],
			}],
		}));

		const worktreeCalls = fake.callsTo(c => c.url.endsWith('/worktrees') && c.init?.method === 'POST');
		const branches = worktreeCalls.map(c => JSON.parse(c.init!.body as string).branch);
		expect(branches).toEqual([
			'repro-tests/a-m/1',
			'repro-tests/a-m/2',
			'repro-tests/b-m',
		]);

		const chatCalls = fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST');
		const titles = chatCalls.map(c => JSON.parse(c.init!.body as string).name);
		expect(titles[0]).toMatch(/#1/);
		expect(titles[1]).toMatch(/#2/);
		expect(titles[2]).not.toMatch(/#/);
	});

	test('multi-group prefix: branches gain g{n}/ prefix when groups.length > 1', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });

		await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [
				{ prompt: 'p1', models: [{ providerID: 'a', modelID: 'm' }] },
				{ prompt: 'p2', models: [{ providerID: 'b', modelID: 'n' }] },
			],
		}));

		const branches = fake
			.callsTo(c => c.url.endsWith('/worktrees') && c.init?.method === 'POST')
			.map(c => JSON.parse(c.init!.body as string).branch);
		expect(branches).toEqual(['g1/repro-tests/a-m', 'g2/repro-tests/b-n']);
	});

	// ── Failure handling ──────────────────────────────────────────

	test('worktree failure for one model skips that model but continues with the rest', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });

		let worktreeAttempt = 0;
		fake.respond = (req) => {
			if (req.url.endsWith('/worktrees') && req.init?.method === 'POST') {
				worktreeAttempt++;
				if (worktreeAttempt === 1) return json({ error: 'busy' }, 409);
			}
			return fake.defaultRespond(req);
		};

		const result = await useMultiRunStore.getState().createMultiRun(makeParams());

		expect(result?.sessionIds.length).toBe(1); // only the second model survived
		const chats = fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST');
		expect(chats.length).toBe(1); // first model never got to chat creation
	});

	test('chat failure for one model skips that model but continues with the rest', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });
		fake.respond = (req) => {
			if (req.url.includes('/git/status')) return json({ isGitRepo: false });
			if (req.url.endsWith('/chats') && req.init?.method === 'POST') {
				const body = JSON.parse(req.init!.body as string) as { name?: string };
				if (body.name?.includes('openai/gpt-5')) return json({ error: 'reject' }, 500);
			}
			return fake.defaultRespond(req);
		};

		const result = await useMultiRunStore.getState().createMultiRun(makeParams());

		expect(result?.sessionIds.length).toBe(1);
		expect(useMultiRunStore.getState().error).toBeNull(); // partial failures don't poison state
	});

	test('all models fail → returns null, error set to "Failed to create any sessions", no multi-run POST', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });
		fake.respond = (req) => {
			if (req.url.includes('/git/status')) return json({ isGitRepo: false });
			if (req.url.endsWith('/chats')) return json({ error: 'down' }, 500);
			return fake.defaultRespond(req);
		};

		const result = await useMultiRunStore.getState().createMultiRun(makeParams());
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('Failed to create any sessions');
		expect(useMultiRunStore.getState().isLoading).toBe(false);
		expect(fake.callsTo(c => c.url.endsWith('/multi-run'))).toEqual([]);
	});

	test('multi-run fan-out failure surfaces as state.error but does not roll back chats', async () => {
		fake.respond = (req) => {
			if (req.url.endsWith('/multi-run')) return json({ error: 'queue full' }, 503);
			return fake.defaultRespond(req);
		};

		const result = await useMultiRunStore.getState().createMultiRun(makeParams());
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('queue full');
		expect(useMultiRunStore.getState().isLoading).toBe(false);

		// Chats *did* get created. That's intentional — we don't auto-delete on fan-out failure
		// because the user might want to drive them by hand.
		expect(fake.callsTo(c => c.url.endsWith('/chats') && c.init?.method === 'POST').length).toBe(2);
	});

	test('git-status probe failure falls back to no-isolation but still proceeds', async () => {
		useProjectsStore.setState({ projects: [makeProject() as never], activeProjectId: 'proj-1' });
		fake.respond = (req) => {
			if (req.url.includes('/git/status')) return json({ error: 'permission denied' }, 500);
			return fake.defaultRespond(req);
		};

		const result = await useMultiRunStore.getState().createMultiRun(makeParams());

		expect(result?.sessionIds.length).toBe(2);
		expect(fake.callsTo(c => c.url.endsWith('/worktrees'))).toEqual([]);
	});

	test('clearError resets the error slot', () => {
		useMultiRunStore.setState({ error: 'boom' });
		useMultiRunStore.getState().clearError();
		expect(useMultiRunStore.getState().error).toBeNull();
	});
});
