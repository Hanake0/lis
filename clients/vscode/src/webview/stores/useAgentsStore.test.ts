import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import {
	filterVisibleAgents,
	isAgentBuiltIn,
	isAgentHidden,
	useAgentsStore,
} from './useAgentsStore';

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
		new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } });

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

describe('useAgentsStore', () => {
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
		useAgentsStore.setState({ agents: [], isLoading: false, selectedAgentName: null, agentDraft: null, lastError: null });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('loadAgents GETs /api/agents and maps the snake_case DTOs to AgentWithExtras', async () => {
		fake.respond = () => jsonResponse([
			{ name: 'opus',    model: 'anthropic/claude-opus-4', display_name: 'Opus', is_default: true,  thinking_effort: 'high' },
			{ name: 'helper',  model: 'openai/gpt-5',            display_name: 'Helper', is_default: false },
		]);
		const ok = await useAgentsStore.getState().loadAgents();
		expect(ok).toBe(true);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/agents');

		const agents = useAgentsStore.getState().agents;
		expect(agents.length).toBe(2);
		expect(agents[0].name).toBe('opus');
		expect(agents[0].native).toBe(true);     // is_default carries through to native
		expect(agents[1].native).toBe(false);
	});

	test('loadAgents returns false and stores lastError on failure', async () => {
		fake.respond = () => jsonResponse({ error: 'database down' }, 500);
		const ok = await useAgentsStore.getState().loadAgents();
		expect(ok).toBe(false);
		expect(useAgentsStore.getState().lastError).toContain('database down');
	});

	test('loadAgents coalesces concurrent loads — only one fetch even if invoked in parallel', async () => {
		let resolveFetch!: (r: Response) => void;
		fake.respond = () => new Promise<Response>(r => { resolveFetch = r; });

		const p1 = useAgentsStore.getState().loadAgents();
		// Trigger a second call while the first is in-flight — should short-circuit to true.
		const p2 = useAgentsStore.getState().loadAgents();
		resolveFetch(jsonResponse([]));
		const [a, b] = await Promise.all([p1, p2]);
		expect(a).toBe(true);
		expect(b).toBe(true);
		expect(fake.calls.length).toBe(1);
	});

	test('getAgentByName + getVisibleAgents respect hidden flag', () => {
		useAgentsStore.setState({ agents: [
			{ name: 'visible', model: 'm' } as never,
			{ name: 'hidden',  model: 'm', hidden: true } as never,
			{ name: 'opt-hidden', model: 'm', options: { hidden: true } } as never,
		] });
		expect(useAgentsStore.getState().getAgentByName('visible')?.name).toBe('visible');
		expect(useAgentsStore.getState().getAgentByName('missing')).toBeUndefined();
		expect(useAgentsStore.getState().getVisibleAgents().map(a => a.name)).toEqual(['visible']);
	});

	test('setSelectedAgent + setAgentDraft are pass-through writes', () => {
		useAgentsStore.getState().setSelectedAgent('opus');
		expect(useAgentsStore.getState().selectedAgentName).toBe('opus');

		const draft = { name: 'd', scope: 'user' as const };
		useAgentsStore.getState().setAgentDraft(draft);
		expect(useAgentsStore.getState().agentDraft).toEqual(draft);

		useAgentsStore.getState().setSelectedAgent(null);
		useAgentsStore.getState().setAgentDraft(null);
		expect(useAgentsStore.getState().selectedAgentName).toBeNull();
		expect(useAgentsStore.getState().agentDraft).toBeNull();
	});

	test('createAgent / updateAgent / deleteAgent throw a clear "not wired" error', async () => {
		await expect(useAgentsStore.getState().createAgent({ name: 'x' })).rejects.toThrow(/not wired in Lis yet/);
		await expect(useAgentsStore.getState().updateAgent('x', { description: 'y' })).rejects.toThrow(/not wired/);
		await expect(useAgentsStore.getState().deleteAgent('x')).rejects.toThrow(/not wired/);
	});

	// ── Pure helpers ──────────────────────────────────────────────

	test('isAgentBuiltIn honours native, builtIn, and Lis-native is_default', () => {
		expect(isAgentBuiltIn({ name: 'a', model: 'm' } as never)).toBe(false);
		expect(isAgentBuiltIn({ name: 'a', model: 'm', native:     true } as never)).toBe(true);
		expect(isAgentBuiltIn({ name: 'a', model: 'm', builtIn:    true } as never)).toBe(true);
		expect(isAgentBuiltIn({ name: 'a', model: 'm', is_default: true } as never)).toBe(true);
	});

	test('isAgentHidden honours both top-level hidden and options.hidden', () => {
		expect(isAgentHidden({ name: 'a', model: 'm' } as never)).toBe(false);
		expect(isAgentHidden({ name: 'a', model: 'm', hidden: true } as never)).toBe(true);
		expect(isAgentHidden({ name: 'a', model: 'm', options: { hidden: true } } as never)).toBe(true);
	});

	test('filterVisibleAgents drops only hidden agents', () => {
		const filtered = filterVisibleAgents([
			{ name: 'a', model: 'm' } as never,
			{ name: 'b', model: 'm', hidden: true } as never,
			{ name: 'c', model: 'm' } as never,
		]);
		expect(filtered.map(a => a.name)).toEqual(['a', 'c']);
	});
});
