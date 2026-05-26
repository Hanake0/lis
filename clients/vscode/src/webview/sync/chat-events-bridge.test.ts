import { beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useGlobalSessionsStore } from '@/stores/useGlobalSessionsStore';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { mountChatEventsBridge } from './chat-events-bridge';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_t: 'message', h: (e: { data: unknown }) => void): void { this.handlers.push(h); }
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

interface RecordedEsHandler { type: string; handler: (ev: MessageEvent) => void }

class FakeEventSource implements EventSourceLike {
	static last: FakeEventSource | null = null;
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	readonly handlers: RecordedEsHandler[] = [];
	constructor(_url: string) { FakeEventSource.last = this; }
	close(): void { /* noop */ }
	addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
		this.handlers.push({ type, handler });
	}
	fire(type: string, data: unknown, id?: string): void {
		for (const h of this.handlers) {
			if (h.type === type) h.handler({ data, lastEventId: id ?? '' } as MessageEvent);
		}
	}
}

function chatPayload(over: Partial<{
	chat_id: string; name: string | null; project_id: string | null;
	pinned_at: string | null; archived_at: string | null;
	created_at: string; updated_at: string;
}> = {}): unknown {
	return {
		chat: {
			chat_id:     over.chat_id     ?? 'c1',
			name:        over.name        ?? 'Test Chat',
			project_id:  over.project_id  ?? null,
			pinned_at:   over.pinned_at   ?? null,
			archived_at: over.archived_at ?? null,
			created_at:  over.created_at  ?? '2026-01-01T00:00:00Z',
			updated_at:  over.updated_at  ?? '2026-01-02T00:00:00Z',
		},
	};
}

describe('mountChatEventsBridge', () => {
	let source: FakeMessageSource;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		FakeEventSource.last = null;
		source = new FakeMessageSource();
		LisRpcClient.install(source, { eventSourceFactory: (url) => new FakeEventSource(url) });
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
		useGlobalSessionsStore.setState({
			activeSessions: [], archivedSessions: [],
			sessionsByDirectory: new Map(), hasLoaded: false, status: 'idle',
		});
	});

	test('chat_created upserts the session into the active bucket', () => {
		const unsub = mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', name: 'A' })));
		const ids = useGlobalSessionsStore.getState().activeSessions.map(s => s.id);
		expect(ids).toEqual(['a']);
		unsub();
	});

	test('chat_updated replaces the existing row in place (same id, refreshed name)', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', name: 'Old' })));
		FakeEventSource.last!.fire('chat_updated', JSON.stringify(chatPayload({ chat_id: 'a', name: 'New' })));
		const sessions = useGlobalSessionsStore.getState().activeSessions;
		expect(sessions.length).toBe(1);
		expect(sessions[0].title).toBe('New');
	});

	test('chat_archived moves the session into the archived bucket', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a' })));
		FakeEventSource.last!.fire('chat_archived', JSON.stringify(chatPayload({ chat_id: 'a', archived_at: '2026-01-03T00:00:00Z' })));
		const s = useGlobalSessionsStore.getState();
		expect(s.activeSessions).toEqual([]);
		expect(s.archivedSessions.map(x => x.id)).toEqual(['a']);
	});

	test('chat_deleted drops the session from both buckets', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a' })));
		FakeEventSource.last!.fire('chat_deleted', JSON.stringify({ chat_id: 'a' }));
		expect(useGlobalSessionsStore.getState().activeSessions).toEqual([]);
	});

	test('unrelated events are ignored — bridge does not touch the store', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a' })));
		FakeEventSource.last!.fire('project_created', JSON.stringify({ project: { id: 'p1' } }));
		expect(useGlobalSessionsStore.getState().activeSessions.length).toBe(1);
	});

	test('malformed chat payloads are silently dropped — no throw, no state change', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify({ chat: { wrong_shape: true } }));
		FakeEventSource.last!.fire('chat_created', JSON.stringify({}));
		FakeEventSource.last!.fire('chat_created', 'not even json');
		expect(useGlobalSessionsStore.getState().activeSessions).toEqual([]);
	});

	test('chat_deleted with missing chat_id is silently dropped', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a' })));
		FakeEventSource.last!.fire('chat_deleted', JSON.stringify({}));
		// Still there — the malformed delete didn't accidentally clear the row.
		expect(useGlobalSessionsStore.getState().activeSessions.length).toBe(1);
	});

	test('returned unsubscribe stops the bridge — further events are ignored', () => {
		const unsub = mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a' })));
		unsub();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'b' })));
		const ids = useGlobalSessionsStore.getState().activeSessions.map(s => s.id);
		expect(ids).toEqual(['a']); // b was ignored
	});

	test('chat_moved patches projectId + re-buckets directory for an existing row', () => {
		mountChatEventsBridge();
		// Seed projects so resolveGlobalSessionDirectory can map projectId → path.
		useProjectsStore.setState({
			projects: [
				{ id: 'p1', path: '/proj/one' } as never,
				{ id: 'p2', path: '/proj/two' } as never,
			],
		} as never);
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', project_id: 'p1' })));
		FakeEventSource.last!.fire('chat_moved',   JSON.stringify({ chat_id: 'a', project_id: 'p2' }));
		const s = useGlobalSessionsStore.getState().activeSessions.find(x => x.id === 'a');
		expect(s).toBeTruthy();
		expect((s as unknown as { projectId: string | null }).projectId).toBe('p2');
		expect((s as unknown as { directory: string | null }).directory).toBe('/proj/two');
	});

	test('chat_moved with project_id=null detaches the chat from any project', () => {
		mountChatEventsBridge();
		useProjectsStore.setState({ projects: [{ id: 'p1', path: '/proj/one' } as never] } as never);
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', project_id: 'p1' })));
		FakeEventSource.last!.fire('chat_moved',   JSON.stringify({ chat_id: 'a', project_id: null }));
		const s = useGlobalSessionsStore.getState().activeSessions.find(x => x.id === 'a');
		expect(s).toBeTruthy();
		expect((s as unknown as { projectId: string | null }).projectId).toBeNull();
	});

	test('chat_moved for unknown chat is silently dropped — store untouched', () => {
		mountChatEventsBridge();
		useProjectsStore.setState({ projects: [{ id: 'p1', path: '/proj/one' } as never] } as never);
		FakeEventSource.last!.fire('chat_moved', JSON.stringify({ chat_id: 'never-seen', project_id: 'p1' }));
		expect(useGlobalSessionsStore.getState().activeSessions).toEqual([]);
	});

	test('chat_moved with malformed payload is silently dropped', () => {
		mountChatEventsBridge();
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', project_id: 'p1' })));
		FakeEventSource.last!.fire('chat_moved',   JSON.stringify({ wrong: true }));
		FakeEventSource.last!.fire('chat_moved',   'not even json');
		const s = useGlobalSessionsStore.getState().activeSessions.find(x => x.id === 'a');
		expect((s as unknown as { projectId: string | null }).projectId).toBe('p1');
	});

	test('chat_moved without a project_id field (backend WhenWritingNull omits it) detaches the chat', () => {
		// JsonOpt.Default has DefaultIgnoreCondition=WhenWritingNull, so a chat
		// detached to no-project arrives as `{ chat_id }` only — no project_id
		// key. The bridge must treat that as null.
		mountChatEventsBridge();
		useProjectsStore.setState({ projects: [{ id: 'p1', path: '/proj/one' } as never] } as never);
		FakeEventSource.last!.fire('chat_created', JSON.stringify(chatPayload({ chat_id: 'a', project_id: 'p1' })));
		FakeEventSource.last!.fire('chat_moved',   JSON.stringify({ chat_id: 'a' /* project_id intentionally omitted */ }));
		const s = useGlobalSessionsStore.getState().activeSessions.find(x => x.id === 'a');
		expect(s).toBeTruthy();
		expect((s as unknown as { projectId: string | null }).projectId).toBeNull();
	});
});
