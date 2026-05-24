import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useSessionUIStore } from '@/sync/session-ui-store';
import {
	applyGitStash,
	checkIsGitRepository,
	countGitStashFiles,
	deleteGitBranch,
	dropGitStash,
	generateCommitMessage,
	generatePullRequestDescription,
	getCommitFileDiff,
	getConflictDetails,
	getGitBranches,
	getGitWorktreeBootstrapStatus,
	listGitStashes,
	popGitStash,
	renameBranch,
	setGitWorktreeBootstrapStatus,
	stashGitChanges,
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

describe('gitApi (read-only repo introspection)', () => {
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

	test('checkIsGitRepository returns false on backend failure', async () => {
		fake.respond = () => json({ error: 'invalid path' }, 400);
		expect(await checkIsGitRepository('/bad')).toBe(false);
	});

	test('getGitBranches returns the backend payload', async () => {
		fake.respond = () => json({ local: ['main', 'dev'], remote: ['origin/main'], current: 'main' });
		expect(await getGitBranches('/repo')).toEqual({ local: ['main', 'dev'], remote: ['origin/main'], current: 'main' });
	});

	test('getGitBranches returns null on backend failure', async () => {
		fake.respond = () => json({ error: 'busy' }, 500);
		expect(await getGitBranches('/repo')).toBeNull();
	});
});

describe('gitApi (branch CRUD)', () => {
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

	test('deleteGitBranch issues DELETE with path + force in the query string', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		const res = await deleteGitBranch('/repo', { branch: 'feature', force: true });

		expect(res).toEqual({ success: true });
		const call = fake.calls[0];
		expect(call.init?.method).toBe('DELETE');
		expect(call.url).toContain('/api/git/branches/feature');
		expect(call.url).toContain('path=%2Frepo');
		expect(call.url).toContain('force=true');
	});

	test('deleteGitBranch surfaces backend 409 (not fully merged) as a thrown LisRpcError', async () => {
		fake.respond = () => json({ title: 'branch not fully merged', detail: 'branch X is not fully merged into HEAD' }, 409);
		await expect(deleteGitBranch('/repo', { branch: 'side' })).rejects.toThrow();
	});

	test('renameBranch issues PATCH with new_name body and returns the renamed ref', async () => {
		fake.respond = () => json({ name: 'better', tip: 'abc' });
		const res = await renameBranch('/repo', 'old', 'better');

		expect(res).toEqual({ success: true, branch: 'better' });
		const call = fake.calls[0];
		expect(call.init?.method).toBe('PATCH');
		expect(call.url).toContain('/api/git/branches/old');
		expect(JSON.parse(call.init!.body as string)).toEqual({ path: '/repo', new_name: 'better' });
	});

	test('deleteGitBranch / renameBranch reject when args are empty', async () => {
		await expect(deleteGitBranch('', { branch: 'x' })).rejects.toThrow();
		await expect(deleteGitBranch('/r', { branch: '' })).rejects.toThrow();
		await expect(renameBranch('', 'a', 'b')).rejects.toThrow();
		await expect(renameBranch('/r', '', 'b')).rejects.toThrow();
		await expect(renameBranch('/r', 'a', '')).rejects.toThrow();
	});
});

describe('gitApi (stash)', () => {
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

	test('listGitStashes maps snake_case wire to GitStashEntry shape', async () => {
		fake.respond = () => json({
			stashes: [{ index: 0, ref: 'stash@{0}', hash: 'sha1', message: 'wip', at: '2026-01-01T00:00:00Z', relative_time: '5m ago' }],
		});
		const result = await listGitStashes('/repo');
		expect(result.stashes).toEqual([{ ref: 'stash@{0}', message: 'wip', relativeTime: '5m ago', hash: 'sha1' }]);
	});

	test('listGitStashes returns empty list on empty path / failure', async () => {
		expect(await listGitStashes('')).toEqual({ stashes: [] });
		fake.respond = () => json({ error: 'down' }, 500);
		expect(await listGitStashes('/repo')).toEqual({ stashes: [] });
	});

	test('stashGitChanges POSTs include_untracked=true by default', async () => {
		fake.respond = () => json({ success: true, created: true, ref: 'stash@{0}', message: 'wip', output: 'stash saved' });
		const res = await stashGitChanges('/repo', { message: 'wip' });

		expect(res.success).toBe(true);
		expect(res.created).toBe(true);
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body).toEqual({ path: '/repo', message: 'wip', include_untracked: true });
	});

	test('stashGitChanges relays created=false when working tree was clean', async () => {
		fake.respond = () => json({ success: true, created: false, ref: null, message: 'no changes to stash', output: 'no changes to stash' });
		const res = await stashGitChanges('/repo');
		expect(res.created).toBe(false);
	});

	test('applyGitStash / popGitStash / dropGitStash hit the right verbs + paths', async () => {
		fake.respond = (req) => {
			if (req.init?.method === 'DELETE') return json({ success: true, ref: 'stash@{0}' });
			return json({ success: true, ref: 'stash@{0}' });
		};

		await applyGitStash('/repo', { ref: 'stash@{0}' });
		await popGitStash('/repo', { ref: 'stash@{0}' });
		await dropGitStash('/repo', { ref: 'stash@{0}' });

		expect(fake.calls[0].init?.method).toBe('POST');
		expect(fake.calls[0].url).toContain('/stash%40%7B0%7D/apply');
		expect(fake.calls[0].url).toContain('path=%2Frepo');
		expect(fake.calls[1].init?.method).toBe('POST');
		expect(fake.calls[1].url).toContain('/stash%40%7B0%7D/pop');
		expect(fake.calls[2].init?.method).toBe('DELETE');
		expect(fake.calls[2].url).toContain('/stash%40%7B0%7D');
	});

	test('countGitStashFiles returns empty when refs list is empty', async () => {
		expect(await countGitStashFiles('/repo', [])).toEqual({ counts: {} });
	});

	test('countGitStashFiles returns the wire counts when present', async () => {
		fake.respond = () => json({ counts: { 'stash@{0}': 3, 'stash@{1}': 1 } });
		const res = await countGitStashFiles('/repo', ['stash@{0}', 'stash@{1}']);
		expect(res.counts['stash@{0}']).toBe(3);
		expect(res.counts['stash@{1}']).toBe(1);
	});
});

describe('gitApi (diff + conflict)', () => {
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

	test('getCommitFileDiff maps snake_case is_binary to camelCase isBinary', async () => {
		fake.respond = () => json({ original: 'a', modified: 'b', is_binary: false });
		const res = await getCommitFileDiff('/repo', 'abc', 'file.ts');
		expect(res).toEqual({ original: 'a', modified: 'b', isBinary: false });
	});

	test('getConflictDetails returns empty payload when backend returns 204', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		const res = await getConflictDetails('/repo');
		expect(res.unmergedFiles).toEqual([]);
		expect(res.diff).toBe('');
	});

	test('getConflictDetails maps wire to the OC shape (camelCase)', async () => {
		fake.respond = () => json({
			operation: 'merge',
			status_porcelain: 'UU file\n',
			unmerged_files: ['file.ts'],
			diff: '<<<<<<<\n=======\n>>>>>>>\n',
			head_info: 'abc subject',
		});
		const res = await getConflictDetails('/repo');
		expect(res.operation).toBe('merge');
		expect(res.statusPorcelain).toContain('UU');
		expect(res.unmergedFiles).toEqual(['file.ts']);
		expect(res.headInfo).toBe('abc subject');
	});
});

describe('gitApi (worktree bootstrap)', () => {
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

	test('getGitWorktreeBootstrapStatus returns ready when backend reports ready', async () => {
		fake.respond = () => json({ status: 'ready', error: null, updated_at: '2026-01-01T00:00:00Z' });
		const res = await getGitWorktreeBootstrapStatus('/repo/.worktrees/x');
		expect(res.status).toBe('ready');
		expect(res.error).toBeNull();
		expect(res.updatedAt).toBeGreaterThan(0);
	});

	test('getGitWorktreeBootstrapStatus normalises unknown statuses to ready', async () => {
		fake.respond = () => json({ status: 'mystery', error: null, updated_at: '2026-01-01T00:00:00Z' });
		const res = await getGitWorktreeBootstrapStatus('/wt');
		expect(res.status).toBe('ready');
	});

	test('getGitWorktreeBootstrapStatus returns ready/null on failure', async () => {
		fake.respond = () => json({ error: 'nope' }, 500);
		expect(await getGitWorktreeBootstrapStatus('/wt')).toEqual({ status: 'ready', error: null, updatedAt: 0 });
	});

	test('setGitWorktreeBootstrapStatus PUTs the right payload', async () => {
		fake.respond = () => json({ status: 'pending', error: null, updated_at: '2026-01-01T00:00:00Z' });
		await setGitWorktreeBootstrapStatus('/wt', 'pending');
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body).toEqual({ worktree_path: '/wt', status: 'pending', error: null });
	});
});

describe('gitApi (AI generation)', () => {
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
		useSessionUIStore.setState({ currentSessionId: null });
	});

	afterEach(() => {
		globalThis.fetch = originalFetch;
		useSessionUIStore.setState({ currentSessionId: null });
	});

	test('generateCommitMessage throws when no chat is selected', async () => {
		await expect(generateCommitMessage('/repo', ['a.ts'])).rejects.toThrow(/existing chat/);
	});

	test('generateCommitMessage parses { subject, highlights } JSON out of the assistant text', async () => {
		useSessionUIStore.setState({ currentSessionId: 'chat-1' });
		fake.respond = () => json({
			text: 'Here it is:\n{"subject":"✨ feat: add foo","highlights":["a","b"]}',
			input_tokens: 10,
			output_tokens: 20,
		});

		const res = await generateCommitMessage('/repo', ['a.ts', 'b.ts'], { model: 'claude-sonnet-4' });
		expect(res.message.subject).toBe('✨ feat: add foo');
		expect(res.message.highlights).toEqual(['a', 'b']);

		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.kind).toBe('commit');
		expect(body.model).toBe('claude-sonnet-4');
		expect(body.visible_prompt).toContain('a.ts');
		expect(body.visible_prompt).toContain('b.ts');
		expect(body.hidden_prompt).toContain('JSON');
	});

	test('generateCommitMessage throws when the model returned no JSON', async () => {
		useSessionUIStore.setState({ currentSessionId: 'chat-1' });
		fake.respond = () => json({ text: 'no json here', input_tokens: 0, output_tokens: 0 });
		await expect(generateCommitMessage('/repo', ['a.ts'])).rejects.toThrow();
	});

	test('generateCommitMessage tolerates a fenced JSON block', async () => {
		useSessionUIStore.setState({ currentSessionId: 'chat-1' });
		fake.respond = () => json({
			text: 'sure:\n```json\n{"subject":"chore: cleanup","highlights":[]}\n```',
			input_tokens: 0,
			output_tokens: 0,
		});
		const res = await generateCommitMessage('/repo', ['a.ts']);
		expect(res.message.subject).toBe('chore: cleanup');
	});

	test('generatePullRequestDescription parses { title, body } and surfaces base→head context', async () => {
		useSessionUIStore.setState({ currentSessionId: 'chat-1' });
		fake.respond = () => json({
			text: '{"title":"My PR","body":"## Summary\\n- stuff"}',
			input_tokens: 0,
			output_tokens: 0,
		});

		const res = await generatePullRequestDescription('/repo', { base: 'main', head: 'feature' });
		expect(res.title).toBe('My PR');
		expect(res.body).toContain('Summary');

		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.visible_prompt).toContain('feature');
		expect(body.visible_prompt).toContain('main');
	});

	test('generatePullRequestDescription throws when no chat is selected', async () => {
		await expect(generatePullRequestDescription('/repo', { base: 'main', head: 'feature' })).rejects.toThrow(/existing chat/);
	});
});
