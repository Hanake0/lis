import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	createProjectPlanFile,
	deleteProjectPlanFile,
	getProjectNotesAndTodos,
	getWorktreeSetupCommands,
	OPENCHAMBER_PROJECT_NOTES_MAX_LENGTH,
	parseProjectPlanMarkdown,
	readProjectPlanFile,
	saveProjectNotesAndTodos,
	saveWorktreeSetupCommands,
} from './openchamberConfig';

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

interface RecordedCall { url: string; init?: RequestInit; key?: string }

class FakeFetch {
	calls: RecordedCall[] = [];
	store = new Map<string, string>();
	/** Override per test for explicit error cases; default reads/writes the in-memory store. */
	respond: ((req: { url: string; init?: RequestInit }) => Response | Promise<Response>) | null = null;

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			if (this.respond) return this.respond({ url, init });
			return this.defaultRespond(url, init);
		};
	}

	defaultRespond(url: string, init?: RequestInit): Response {
		const method = init?.method ?? 'GET';
		const m = /\/api\/projects\/([^/]+)\/metadata(?:\/([^/?]+))?$/.exec(url);
		if (!m) return new Response('', { status: 404 });
		const projectId = decodeURIComponent(m[1]);
		const key       = m[2] ? decodeURIComponent(m[2]) : null;
		const slot      = `${projectId}:${key}`;

		if (method === 'GET' && key) {
			const value = this.store.get(slot);
			return json(value == null
				? { key, value: null, found: false, updated_at: null }
				: { key, value,       found: true,  updated_at: 'now' });
		}
		if (method === 'PUT' && key) {
			const body = JSON.parse(init!.body as string);
			this.store.set(slot, body.value);
			return json({ key, value: body.value, updated_at: 'now' });
		}
		if (method === 'DELETE' && key) {
			this.store.delete(slot);
			return new Response(null, { status: 204 });
		}
		return new Response('', { status: 404 });
	}
}

function json(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

describe('openchamberConfig', () => {
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

	// ── Notes + todos ─────────────────────────────────────────────

	test('getProjectNotesAndTodos returns empty defaults when nothing is saved', async () => {
		const data = await getProjectNotesAndTodos({ id: 'p1' });
		expect(data).toEqual({ notes: '', todos: [] });
	});

	test('saveProjectNotesAndTodos persists both keys, getter round-trips them', async () => {
		const todos = [{ id: 't1', text: 'do this', completed: false, createdAt: 'now' }];
		const save = await saveProjectNotesAndTodos({ id: 'p1' }, { notes: 'hello', todos });
		expect(save).toEqual({ ok: true });

		const loaded = await getProjectNotesAndTodos({ id: 'p1' });
		expect(loaded.notes).toBe('hello');
		expect(loaded.todos).toEqual(todos);
	});

	test('saveProjectNotesAndTodos enforces the notes length limit', async () => {
		const tooLong = 'x'.repeat(OPENCHAMBER_PROJECT_NOTES_MAX_LENGTH + 1);
		const res = await saveProjectNotesAndTodos({ id: 'p1' }, { notes: tooLong });
		expect(res.ok).toBe(false);
		expect(res.failed).toContain('notes-too-long');
	});

	test('saveProjectNotesAndTodos accepts string project id directly', async () => {
		await saveProjectNotesAndTodos('p1', { notes: 'x' });
		const data = await getProjectNotesAndTodos('p1');
		expect(data.notes).toBe('x');
	});

	test('corrupted JSON in todos slot falls back to empty array', async () => {
		fake.store.set('p1:todos', 'not json');
		const data = await getProjectNotesAndTodos({ id: 'p1' });
		expect(data.todos).toEqual([]);
	});

	// ── Worktree setup commands ───────────────────────────────────

	test('getWorktreeSetupCommands returns [] when unset', async () => {
		expect(await getWorktreeSetupCommands('p1')).toEqual([]);
	});

	test('saveWorktreeSetupCommands persists JSON array; getter parses + filters strings', async () => {
		await saveWorktreeSetupCommands('p1', ['npm install', 'npm run build']);
		expect(await getWorktreeSetupCommands('p1')).toEqual(['npm install', 'npm run build']);
	});

	test('getWorktreeSetupCommands tolerates malformed stored value', async () => {
		fake.store.set('p1:setupCommands', '{}');
		expect(await getWorktreeSetupCommands('p1')).toEqual([]);
	});

	// ── Plan file ─────────────────────────────────────────────────

	test('readProjectPlanFile returns null when unset', async () => {
		expect(await readProjectPlanFile('p1')).toBeNull();
	});

	test('createProjectPlanFile persists + readProjectPlanFile reads it back', async () => {
		const created = await createProjectPlanFile('p1', '# Plan\nbody');
		expect(created?.body).toBe('# Plan\nbody');
		const read = await readProjectPlanFile('p1');
		expect(read?.body).toBe('# Plan\nbody');
	});

	test('deleteProjectPlanFile clears it', async () => {
		await createProjectPlanFile('p1', 'body');
		expect(await deleteProjectPlanFile('p1')).toBe(true);
		expect(await readProjectPlanFile('p1')).toBeNull();
	});

	// ── parseProjectPlanMarkdown ─────────────────────────────────

	test('parseProjectPlanMarkdown returns empty sections for empty input', () => {
		expect(parseProjectPlanMarkdown('')).toEqual({ sections: [] });
		expect(parseProjectPlanMarkdown('   ')).toEqual({ sections: [] });
	});

	test('parseProjectPlanMarkdown splits by `#`, `##`, `###` headings', () => {
		const md = '# A\nbody a\n## B\nbody b\n### C\nbody c';
		expect(parseProjectPlanMarkdown(md)).toEqual({
			sections: [
				{ heading: 'A', content: 'body a' },
				{ heading: 'B', content: 'body b' },
				{ heading: 'C', content: 'body c' },
			],
		});
	});

	test('parseProjectPlanMarkdown ignores h4+ headings (kept as content)', () => {
		const md = '# A\n#### nested\nstill A body';
		const result = parseProjectPlanMarkdown(md);
		expect(result.sections.length).toBe(1);
		expect(result.sections[0].heading).toBe('A');
		expect(result.sections[0].content).toContain('#### nested');
	});

	// ── Edge cases ────────────────────────────────────────────────

	test('null / undefined project resolves to no-op getters', async () => {
		expect(await getProjectNotesAndTodos(null as never)).toEqual({ notes: '', todos: [] });
		expect(await getWorktreeSetupCommands('')).toEqual([]);
		expect(await readProjectPlanFile('')).toBeNull();
	});

	test('saveProjectNotesAndTodos with no project returns ok=false', async () => {
		expect(await saveProjectNotesAndTodos('', { notes: 'x' })).toEqual({ ok: false, failed: ['no project id'] });
	});

	test('backend failure on PUT surfaces as ok=false with the failing key', async () => {
		fake.respond = (req) => {
			if (req.init?.method === 'PUT') return json({ error: 'busy' }, 500);
			return fake.defaultRespond(req.url, req.init);
		};
		const res = await saveProjectNotesAndTodos('p1', { notes: 'x' });
		expect(res).toEqual({ ok: false, failed: ['notes'] });
	});
});
