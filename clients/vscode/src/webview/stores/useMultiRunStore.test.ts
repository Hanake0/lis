import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import type { CreateMultiRunParams } from '@/types/multirun';
import { useMultiRunStore } from './useMultiRunStore';

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
	respond: (req: { url: string; init?: RequestInit }) => Response =
		() => new Response(JSON.stringify({ run_id: 'r1' }), { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

function makeParams(over: Partial<CreateMultiRunParams> = {}): CreateMultiRunParams {
	return {
		name:   'Repro tests',
		groups: [
			{
				prompt: 'Diagnose the flaky test',
				models: [
					{ providerID: 'anthropic', modelID: 'claude-opus-4', variant: 'chat-A' },
					{ providerID: 'openai',    modelID: 'gpt-5',         variant: 'chat-B' },
				],
			},
		],
		agent: 'investigator',
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
	});
	afterEach(() => { globalThis.fetch = originalFetch; });

	test('happy path POSTs /api/multi-run with prompt + resolved targets and returns the slug', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams());
		expect(fake.calls.length).toBe(1);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/multi-run');
		expect(fake.calls[0].init?.method).toBe('POST');
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.prompt).toBe('Diagnose the flaky test');
		expect(body.targets).toEqual([
			{ chat_id: 'chat-A', agent_name: 'investigator', model: 'anthropic/claude-opus-4' },
			{ chat_id: 'chat-B', agent_name: 'investigator', model: 'openai/gpt-5' },
		]);
		expect(result).toEqual({ groupSlug: 'repro-tests', sessionIds: ['chat-A', 'chat-B'], firstSessionId: 'chat-A' });
		expect(useMultiRunStore.getState().isLoading).toBe(false);
		expect(useMultiRunStore.getState().error).toBeNull();
	});

	test('per-group fan-out: one POST per group', async () => {
		const params = makeParams({
			groups: [
				{ prompt: 'P1', models: [{ providerID: 'a', modelID: 'm', variant: 'c1' }] },
				{ prompt: 'P2', models: [{ providerID: 'a', modelID: 'm', variant: 'c2' }] },
			],
		});
		await useMultiRunStore.getState().createMultiRun(params);
		expect(fake.calls.length).toBe(2);
		expect(JSON.parse(fake.calls[0].init!.body as string).prompt).toBe('P1');
		expect(JSON.parse(fake.calls[1].init!.body as string).prompt).toBe('P2');
	});

	test('models without a variant (no targetable chat id) are skipped', async () => {
		await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{
				prompt: 'p',
				models: [
					{ providerID: 'a', modelID: 'm', variant: 'chat-A' },
					{ providerID: 'b', modelID: 'm' /* no variant */ },
				],
			}],
		}));
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.targets.length).toBe(1);
		expect(body.targets[0].chat_id).toBe('chat-A');
	});

	test('reports a clear error when no targetable chats remain', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{ prompt: 'p', models: [{ providerID: 'a', modelID: 'm' }] }],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('No targetable chats');
		expect(fake.calls.length).toBe(0);
	});

	test('rejects an empty group name', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({ name: '   ' }));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('Group name is required');
	});

	test('rejects when groups array is empty', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({ groups: [] }));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('At least one run group');
	});

	test('rejects a group with an empty prompt', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{ prompt: '   ', models: [{ providerID: 'a', modelID: 'm', variant: 'c1' }] }],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('prompt is required');
	});

	test('rejects a group with more than 5 models', async () => {
		const result = await useMultiRunStore.getState().createMultiRun(makeParams({
			groups: [{
				prompt: 'p',
				models: Array.from({ length: 6 }, (_, i) => ({ providerID: 'a', modelID: 'm', variant: `c${i}` })),
			}],
		}));
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('maximum 5 models');
	});

	test('surfaces REST failures as state.error and clears isLoading', async () => {
		fake.respond = () => new Response(JSON.stringify({ error: 'fan-out queue full' }), {
			status: 503, headers: { 'content-type': 'application/json' },
		});
		const result = await useMultiRunStore.getState().createMultiRun(makeParams());
		expect(result).toBeNull();
		expect(useMultiRunStore.getState().error).toContain('fan-out queue full');
		expect(useMultiRunStore.getState().isLoading).toBe(false);
	});

	test('clearError resets the error slot', () => {
		useMultiRunStore.setState({ error: 'boom' });
		useMultiRunStore.getState().clearError();
		expect(useMultiRunStore.getState().error).toBeNull();
	});
});
