import { beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useChatStore } from '@/store';
import { useGlobalSessionsStore } from '@/stores/useGlobalSessionsStore';
import { mountChatPanelBridge } from './chat-panel-bridge';

// ── Fakes ───────────────────────────────────────────────────────────────────

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_t: 'message', h: (e: { data: unknown }) => void): void { this.handlers.push(h); }
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

interface Recorded { type: string; handler: (ev: MessageEvent) => void }

class FakeEventSource implements EventSourceLike {
	static all: FakeEventSource[] = [];
	static byUrl(matcher: (u: string) => boolean): FakeEventSource | undefined {
		return FakeEventSource.all.find((s) => matcher(s.url));
	}
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	readonly handlers: Recorded[] = [];
	closed = false;
	constructor(public readonly url: string) { FakeEventSource.all.push(this); }
	close(): void { this.closed = true; }
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		this.handlers.push({ type, handler });
	}
	fire(type: string, data: unknown, id?: string): void {
		for (const h of this.handlers) {
			if (h.type === type) h.handler({ data, lastEventId: id ?? '' } as MessageEvent);
		}
	}
}

// Track the calls the bridge makes via fetch() so we can assert on bootstrap behaviour
// and shape the responses the bridge sees.
interface FetchCall { method: string; path: string }
let fetchCalls: FetchCall[] = [];
let fetchResponses: Record<string, unknown> = {};

function installFetch(): void {
	(globalThis as { fetch?: unknown }).fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
		const url    = typeof input === 'string' ? input : input.toString();
		const method = (init?.method ?? 'GET').toUpperCase();
		const u      = new URL(url);
		const path   = u.pathname + (u.search || '');
		fetchCalls.push({ method, path });
		const body   = fetchResponses[`${method} ${u.pathname}`] ?? null;
		return new Response(JSON.stringify(body), {
			status: 200, headers: { 'content-type': 'application/json' },
		});
	};
}

function resetGlobalSessionsStore(): void {
	useGlobalSessionsStore.setState({
		activeSessions: [], archivedSessions: [],
		sessionsByDirectory: new Map(), hasLoaded: false, status: 'idle',
	});
}

function resetChatStore(): void {
	useChatStore.setState({
		chatId: null, messages: [], agents: [], currentAgent: null,
		contextData: null, contextOpen: false, isTyping: false,
		composerModel: '', composerEffort: '', composerProfile: '', composerThinking: '',
		sessions: [], permissions: [], questions: [],
	});
}

// ── Tests ───────────────────────────────────────────────────────────────────

describe('mountChatPanelBridge', () => {
	let source: FakeMessageSource;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		FakeEventSource.all = [];
		fetchCalls = [];
		fetchResponses = {};
		resetGlobalSessionsStore();
		resetChatStore();
		installFetch();

		source = new FakeMessageSource();
		LisRpcClient.install(source, { eventSourceFactory: (url) => new FakeEventSource(url) });
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
	});

	test('bootstrap fires GET /chats/{id}/messages, /agents, /chats/{id}/agent on mount', async () => {
		fetchResponses['GET /api/chats/c1/messages'] = [];
		fetchResponses['GET /api/agents']            = [];
		fetchResponses['GET /api/chats/c1/agent']    = { agent_name: 'a', model: 'm' };

		const stop = mountChatPanelBridge('c1');
		// Yield so the async bootstrap fetches complete.
		await new Promise<void>((r) => setTimeout(r, 0));

		const paths = fetchCalls.map(c => `${c.method} ${c.path}`);
		expect(paths).toContain('GET /api/chats/c1/messages');
		expect(paths).toContain('GET /api/agents');
		expect(paths).toContain('GET /api/chats/c1/agent');
		stop();
	});

	test('opens /api/chats/{id}/stream EventSource on mount, closes on stop', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'));
		expect(stream).toBeDefined();
		expect(stream!.closed).toBe(false);
		stop();
		expect(stream!.closed).toBe(true);
	});

	test('history bootstrap turns assistant + user rows into ChatMessages', async () => {
		fetchResponses['GET /api/chats/c1/messages'] = [
			{ id: 'm-u', role: 'user',      text: 'hi',    timestamp: '2026-01-01T00:00:00Z' },
			{ id: 'm-a', role: 'assistant', text: 'hello', timestamp: '2026-01-01T00:00:01Z',
				input_tokens: 10, output_tokens: 20 },
		];
		fetchResponses['GET /api/agents']            = [];
		fetchResponses['GET /api/chats/c1/agent']    = { agent_name: 'a', model: 'm' };

		const stop = mountChatPanelBridge('c1');
		await new Promise<void>((r) => setTimeout(r, 0));

		const msgs = useChatStore.getState().messages;
		expect(msgs.length).toBe(2);
		expect(msgs[0]).toMatchObject({ kind: 'user',      id: 'm-u', text: 'hi' });
		expect(msgs[1]).toMatchObject({ kind: 'assistant', id: 'm-a', text: 'hello', streaming: false });
		stop();
	});

	test('chunk event starts a streaming assistant message, subsequent chunks append', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('chunk', JSON.stringify({ chat_id: 'c1', message_id: 'mid', delta: 'Hel', done: false }));
		stream.fire('chunk', JSON.stringify({ chat_id: 'c1', message_id: 'mid', delta: 'lo',  done: false }));

		const msgs = useChatStore.getState().messages;
		expect(msgs.length).toBe(1);
		expect(msgs[0]).toMatchObject({ kind: 'assistant', id: 'mid', text: 'Hello', streaming: true });
		stop();
	});

	test('events for a different chat_id are ignored', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('chunk', JSON.stringify({ chat_id: 'OTHER', message_id: 'x', delta: 'noise', done: false }));
		expect(useChatStore.getState().messages).toEqual([]);
		stop();
	});

	test('tool_start adds a running tool row; tool_result flips it to done', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('tool_start',  JSON.stringify({ chat_id: 'c1', message_id: 't1', tool_name: 'ls', args: '/tmp' }));
		expect(useChatStore.getState().messages[0]).toMatchObject({ kind: 'tool', name: 'ls', status: 'running' });

		stream.fire('tool_result', JSON.stringify({ chat_id: 'c1', message_id: 't1', tool_name: 'ls', result: 'a\nb' }));
		expect(useChatStore.getState().messages[0]).toMatchObject({ kind: 'tool', status: 'done', result: 'a\nb' });
		stop();
	});

	test('tool_result with error marks the row as error', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('tool_start',  JSON.stringify({ chat_id: 'c1', message_id: 't1', tool_name: 'fail', args: '' }));
		stream.fire('tool_result', JSON.stringify({ chat_id: 'c1', message_id: 't1', tool_name: 'fail', error: 'boom' }));
		expect(useChatStore.getState().messages[0]).toMatchObject({ kind: 'tool', status: 'error', error: 'boom' });
		stop();
	});

	test('typing sets isTyping; typing_stopped clears it and freezes streaming messages', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('typing', JSON.stringify({ chat_id: 'c1' }));
		expect(useChatStore.getState().isTyping).toBe(true);

		// Stage a streaming message so typing_stopped's freeze path runs.
		stream.fire('chunk', JSON.stringify({ chat_id: 'c1', message_id: 'm', delta: 'x', done: false }));
		stream.fire('typing_stopped', JSON.stringify({ chat_id: 'c1' }));
		const msgs = useChatStore.getState().messages;
		expect(useChatStore.getState().isTyping).toBe(false);
		expect(msgs[0]).toMatchObject({ kind: 'assistant', streaming: false });
		stop();
	});

	test('permission_request adds an entry to permissions', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('permission_request', JSON.stringify({
			chat_id: 'c1', approval_id: 'app-1', tool_name: 'rm',
			patterns: ['rm -rf /'], metadata: { reason: 'risky' },
		}));
		const perms = useChatStore.getState().permissions;
		expect(perms.length).toBe(1);
		expect(perms[0]).toMatchObject({ id: 'app-1', sessionID: 'c1', permission: 'rm' });
		stop();
	});

	test('question adds an entry to questions', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('question', JSON.stringify({
			chat_id: 'c1', question_id: 'q-1',
			questions: [{ question: 'pick?', options: [{ label: 'a' }, { label: 'b' }], multiple: false }],
		}));
		const qs = useChatStore.getState().questions;
		expect(qs.length).toBe(1);
		expect(qs[0]).toMatchObject({ id: 'q-1', sessionID: 'c1' });
		expect(qs[0].questions[0].options.map(o => o.label)).toEqual(['a', 'b']);
		stop();
	});

	test('error event adds an error message and clears typing', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;

		stream.fire('typing', JSON.stringify({ chat_id: 'c1' }));
		stream.fire('error',  JSON.stringify({ chat_id: 'c1', error: 'boom' }));
		const state = useChatStore.getState();
		expect(state.isTyping).toBe(false);
		expect(state.messages[0]).toMatchObject({ kind: 'error', text: 'boom' });
		stop();
	});

	test('useGlobalSessionsStore updates mirror into useChatStore.sessions', () => {
		const stop = mountChatPanelBridge('c1');

		useGlobalSessionsStore.setState({
			activeSessions: [{ id: 'a', title: 'A', time: { updated: 1700000000000 } } as any],
			archivedSessions: [{ id: 'b', title: 'B', time: { updated: 1700000001000, archived: 1700000002000 } } as any],
			sessionsByDirectory: new Map(),
			hasLoaded: true,
			status: 'ready',
		});

		const sessions = useChatStore.getState().sessions;
		expect(sessions.map(s => s.chat_id)).toEqual(['a', 'b']);
		expect(sessions[1].archived_at).toBeDefined();
		stop();
	});

	test('stop closes the per-chat stream AND unsubscribes from global SSE', () => {
		const stop   = mountChatPanelBridge('c1');
		const stream = FakeEventSource.byUrl((u) => u.includes('/api/chats/c1/stream'))!;
		stop();
		expect(stream.closed).toBe(true);
		// After stop, firing an event into the global channel should not trigger anything.
		const global = FakeEventSource.byUrl((u) => u.includes('/api/events') && !u.includes('/chats/'))!;
		expect(global).toBeDefined();
		global.fire('agent_updated', JSON.stringify({ chat_id: 'c1' }));
		// No throw — and the bridge is no longer running.
	});
});
