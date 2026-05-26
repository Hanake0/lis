/**
 * Bridges per-chat REST + SSE into {@link useChatStore} for the full chat panel.
 *
 * Mount once per active chatId from the chat-panel React tree:
 *
 *   const stop = mountChatPanelBridge(chatId);
 *   return () => stop();
 *
 * The bridge owns the entire data flow that used to come through the legacy
 * WS-frame postMessage:
 *
 *  - Bootstrap fetches (history + agent list + current agent) via
 *    {@link LisRpcClient}. These hit `GET /api/chats/{id}/messages`,
 *    `GET /api/agents`, `GET /api/chats/{id}/agent`.
 *  - Per-chat SSE subscription via {@link LisChatStreamChannel} on
 *    `/api/chats/{id}/stream`. Each event maps onto a useChatStore mutation
 *    (chunks append to the streaming assistant message, tool_start/result
 *    create/update tool rows, permission_request/question push the prompt
 *    queue, etc.).
 *  - Mirror useGlobalSessionsStore.activeSessions/archivedSessions into
 *    useChatStore.sessions so the panel's session-aware helpers in
 *    `sync-context.ts` keep working without duplicating SSE subscriptions.
 *
 * Returns an unsubscribe that closes the SSE channel and tears down all
 * listeners. Safe to call across chatId changes — the caller passes a fresh
 * chatId, mounts again, and the previous bridge has already cleaned up.
 */

import { useChatStore, type ChatMessage } from '@/store';
import { LisRpcClient, type LisMessageSummary } from '@/lib/lis/rpc-client';
import type { LisChatStreamChannel } from '@/lib/lis/chat-stream-channel';
import { useGlobalSessionsStore } from '@/stores/useGlobalSessionsStore';
import { useSessionUIStore } from './session-ui-store';
import { lisLog } from '@/lib/lis/logger';
import type { AgentSummary, ChatSummary } from '../../types';

/** Wire shape returned by `GET /api/agents`. Mirrors `AgentDto` on the server. */
interface AgentWire {
	name:          string;
	display_name?: string | null;
	model:         string;
	effort?:       string | null;
	tool_profile?: string | null;
	is_default:    boolean;
}

/** Wire shape returned by `GET /api/chats/{id}/agent`. */
interface ChatAgentWire {
	agent_name:    string;
	display_name?: string | null;
	model:         string;
	effort?:       string | null;
	tool_profile?: string | null;
}

/** Per-chat SSE event payloads (mirrors `ChatStreamEvents.cs`). */
interface ChunkData       { chat_id: string; message_id: string; delta: string; done: boolean }
interface ThinkingData    { chat_id: string; message_id: string; delta: string; done: boolean }
interface ToolStartData   { chat_id: string; message_id: string; tool_name: string; args?: string }
interface ToolResultData  { chat_id: string; message_id: string; tool_name: string; result?: string; error?: string | null }
interface TypingData      { chat_id: string }
interface ChatErrorData   { chat_id: string; error: string }
interface MessageEvtData  {
	chat_id: string; id: string; role: 'user' | 'assistant' | 'tool';
	text?: string | null; timestamp: string;
	input_tokens?: number | null; output_tokens?: number | null;
	cache_read_tokens?: number | null; cache_creation_tokens?: number | null;
	thinking_tokens?: number | null;
}
interface MessageTokensData {
	chat_id: string; id: string;
	input_tokens?: number | null; output_tokens?: number | null;
	cache_read_tokens?: number | null; cache_creation_tokens?: number | null;
	thinking_tokens?: number | null;
}
interface PermissionRequestData {
	chat_id: string; approval_id: string; tool_name: string;
	patterns?: string[]; metadata?: Record<string, unknown>; always?: string[];
}
interface QuestionData {
	chat_id: string; question_id: string;
	questions: { question: string; header?: string; multiple?: boolean; options: { label: string; description?: string }[] }[];
}

/**
 * Mount the bridge for one chatId. The caller is responsible for re-mounting
 * when the chatId changes; this bridge does not track chatId transitions.
 *
 * Draft mode (chatId starts with `web:draft-`) skips the history + chat-agent
 * fetches because the row doesn't exist server-side yet — both endpoints
 * would 404. The first user message lazy-creates the row (server side), at
 * which point the host re-mounts the panel with the persisted id and this
 * function re-runs against the real chat.
 */
export function mountChatPanelBridge(chatId: string): () => void {
	const client    = LisRpcClient.instance();
	const cleanups: (() => void)[] = [];

	// ── Bootstrap fetches ─────────────────────────────────────────────
	// Always attempt history + chat-agent. The `web:draft-…` heuristic used to
	// gate this — but persisted chats can also carry a `web:draft-…` id
	// (legacy debug rows + the lazy-create flow keeps the original draft id
	// on first send). The catch blocks in both helpers silently swallow 404s
	// for real drafts, so trying is always safe.
	void loadHistory(client, chatId);
	void loadChatAgent(client, chatId);
	void loadAgents(client);

	// ── Per-chat SSE token stream ─────────────────────────────────────
	const stream = client.openChatStream(chatId);
	stream.onEvent((ev) => handleStreamEvent(chatId, ev.event, ev.data));
	stream.open();
	cleanups.push(() => stream.close());

	// ── Global SSE: agent_updated for live agent rebinding ────────────
	const unsubGlobal = client.onSse((ev) => {
		if (ev.event === 'agent_updated') {
			const data = ev.data as { chat_id?: string } | null;
			if (!data || data.chat_id !== chatId) return;
			void loadChatAgent(client, chatId);
		}
	});
	cleanups.push(unsubGlobal);

	// ── Mirror useGlobalSessionsStore → useChatStore.sessions ─────────
	syncSessions();
	const unsubSessions = useGlobalSessionsStore.subscribe(syncSessions);
	cleanups.push(unsubSessions);

	return () => { for (const fn of cleanups) try { fn(); } catch { /* ignore */ } };
}

// ── Bootstrap helpers ────────────────────────────────────────────────────────

async function loadHistory(client: LisRpcClient, chatId: string): Promise<void> {
	try {
		const rows = await client.getChatMessages(chatId);
		applyHistory(rows);
	} catch (err) {
		// 404s for real drafts are expected — silently swallow. Real failures
		// are logged so we can spot transport issues post-mount.
		lisLog.error(`[chat-panel-bridge] loadHistory failed chat=${chatId} err=${err instanceof Error ? err.message : String(err)}`);
	}
}

async function loadAgents(client: LisRpcClient): Promise<void> {
	try {
		const rows = (await client.listAgents()) as AgentWire[];
		const mapped: AgentSummary[] = rows.map(toAgentSummary);
		const store  = useChatStore.getState();
		store.setAgents(mapped);
		// First-load convenience — if the chat panel hasn't been told who the
		// current agent is yet, fall back to the system default so the composer
		// can render a sensible label.
		if (!store.currentAgent) {
			const def = mapped.find(a => a.is_default) ?? mapped[0];
			if (def) {
				store.setCurrentAgent(def);
				if (def.model) store.setComposerModel(def.model);
			}
		}
	} catch { /* agent picker stays empty */ }
}

async function loadChatAgent(client: LisRpcClient, chatId: string): Promise<void> {
	try {
		const raw  = (await client.getChatAgent(chatId)) as ChatAgentWire;
		const summary: AgentSummary = {
			name:         raw.agent_name,
			display_name: raw.display_name ?? undefined,
			model:        raw.model,
			effort:       raw.effort       ?? undefined,
			tool_profile: raw.tool_profile ?? undefined,
			is_default:   false,
		};
		const store = useChatStore.getState();
		store.setCurrentAgent(summary);
		if (summary.model)        store.setComposerModel(summary.model);
		if (summary.effort)       store.setComposerEffort(summary.effort);
		if (summary.tool_profile) store.setComposerProfile(summary.tool_profile);
	} catch { /* ignore — the global agent list will at least populate the picker */ }
}

function toAgentSummary(w: AgentWire): AgentSummary {
	return {
		name:         w.name,
		display_name: w.display_name ?? undefined,
		model:        w.model,
		effort:       w.effort       ?? undefined,
		tool_profile: w.tool_profile ?? undefined,
		is_default:   w.is_default,
	};
}

// ── Per-chat SSE wiring ──────────────────────────────────────────────────────

function handleStreamEvent(chatId: string, event: string, data: unknown): void {
	switch (event) {
		case 'message':         return handleMessageEvent      (chatId, data as MessageEvtData);
		case 'chunk':           return handleChunkEvent        (chatId, data as ChunkData);
		case 'thinking_chunk':  return handleThinkingChunkEvent(chatId, data as ThinkingData);
		case 'tool_start':      return handleToolStartEvent    (chatId, data as ToolStartData);
		case 'tool_result':     return handleToolResultEvent   (chatId, data as ToolResultData);
		case 'message_tokens':  return handleMessageTokensEvent(chatId, data as MessageTokensData);
		case 'typing':          return handleTypingEvent       (chatId, data as TypingData);
		case 'typing_stopped':  return handleTypingStoppedEvent(chatId, data as TypingData);
		case 'error':           return handleErrorEvent        (chatId, data as ChatErrorData);
		case 'permission_request': return handlePermissionEvent(chatId, data as PermissionRequestData);
		case 'question':        return handleQuestionEvent     (chatId, data as QuestionData);
		default:                return; // reaction/etc. — no UI today
	}
}

function handleMessageEvent(chatId: string, data: MessageEvtData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	const text  = data.text ?? '';

	if (data.role === 'assistant') {
		store.setIsTyping(false);
		const thinkTokens = data.thinking_tokens ?? undefined;

		let curr = store.messages;
		// Stamp the trailing thinking block (if any) with the final thinking-token count.
		if (thinkTokens && thinkTokens > 0) {
			let idx = -1;
			for (let i = curr.length - 1; i >= 0; i--) {
				if (curr[i].kind === 'thinking' && !(curr[i] as { streaming?: boolean }).streaming) { idx = i; break; }
			}
			if (idx >= 0) {
				curr = [...curr];
				const t = curr[idx];
				if (t.kind === 'thinking') curr[idx] = { ...t, tokens: thinkTokens };
			}
		}

		// Replace any in-flight streaming assistant message with the persisted final.
		curr = curr.filter(m => !(m.kind === 'assistant' && m.streaming));
		if (!curr.some(m => m.id === data.id)) {
			curr = [...curr, {
				kind: 'assistant', id: data.id, text, timestamp: data.timestamp, streaming: false,
				tokens: {
					input_tokens:          data.input_tokens          ?? undefined,
					output_tokens:         data.output_tokens         ?? undefined,
					cache_read_tokens:     data.cache_read_tokens     ?? undefined,
					cache_creation_tokens: data.cache_creation_tokens ?? undefined,
					thinking_tokens:       thinkTokens,
				},
			}];
		}
		store.setMessages(curr);
		return;
	}

	if (data.role === 'user' && !store.messages.some(m => m.id === data.id)) {
		store.setMessages([...store.messages, { kind: 'user', id: data.id, text, timestamp: data.timestamp }]);
	}
	// Tool rows arrive via tool_start/tool_result; the persisted `message` echo for
	// role==='tool' is intentionally ignored (no display info beyond what we have).
}

function handleChunkEvent(chatId: string, data: ChunkData): void {
	if (data.chat_id !== chatId) return;
	if (data.done) return;
	const store = useChatStore.getState();
	store.setIsTyping(false);

	const id = data.message_id;
	const existing = store.messages.find(m => m.kind === 'assistant' && (m as { streaming?: boolean }).streaming && m.id === id);
	if (existing) {
		store.setMessages(store.messages.map(m =>
			m.kind === 'assistant' && m.id === id ? { ...m, text: m.text + data.delta } : m,
		));
	} else {
		store.setMessages([...store.messages, {
			kind: 'assistant', id, text: data.delta, timestamp: '', streaming: true,
		}]);
	}
}

function handleThinkingChunkEvent(chatId: string, data: ThinkingData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	const id    = data.message_id;
	store.setIsTyping(false);

	if (data.done) {
		store.setMessages(store.messages.map(m =>
			m.kind === 'thinking' && m.id === id ? { ...m, streaming: false, collapsed: true } : m,
		));
		return;
	}

	if (store.messages.some(m => m.kind === 'thinking' && m.id === id)) {
		store.setMessages(store.messages.map(m =>
			m.kind === 'thinking' && m.id === id
				? { ...m, content: ((m as { content?: string }).content ?? '') + data.delta }
				: m,
		));
	} else {
		store.setMessages([...store.messages, {
			kind: 'thinking', id, content: data.delta, collapsed: false, streaming: true,
		}]);
	}
}

function handleToolStartEvent(chatId: string, data: ToolStartData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	const id    = data.message_id;
	if (store.messages.some(m => m.id === id)) return;
	store.setMessages([...store.messages, {
		kind: 'tool', id, name: data.tool_name, args: data.args, status: 'running',
	}]);
}

function handleToolResultEvent(chatId: string, data: ToolResultData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	const id    = data.message_id;
	const err   = data.error ?? undefined;
	store.setMessages(store.messages.map(m =>
		m.kind === 'tool' && m.id === id
			? { ...m, result: data.result ?? undefined, error: err, status: err ? 'error' : 'done' }
			: m,
	));
}

function handleMessageTokensEvent(chatId: string, data: MessageTokensData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	const tok = data.output_tokens ?? undefined;
	if (typeof tok !== 'number') return;
	store.setMessages(store.messages.map(m =>
		m.kind === 'tool' && m.id === data.id ? { ...m, tokens: tok } : m,
	));
}

function handleTypingEvent(chatId: string, data: TypingData): void {
	if (data.chat_id !== chatId) return;
	useChatStore.getState().setIsTyping(true);
}

function handleTypingStoppedEvent(chatId: string, data: TypingData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	store.setIsTyping(false);
	const hasStreaming = store.messages.some(m =>
		(m.kind === 'assistant' && (m as { streaming?: boolean }).streaming)
		|| (m.kind === 'thinking' && (m as { streaming?: boolean }).streaming),
	);
	if (!hasStreaming) return;
	store.setMessages(store.messages.map(m => {
		if (m.kind === 'assistant' && m.streaming) return { ...m, streaming: false };
		if (m.kind === 'thinking'  && m.streaming) return { ...m, streaming: false, collapsed: true };
		return m;
	}));
}

function handleErrorEvent(chatId: string, data: ChatErrorData): void {
	if (data.chat_id !== chatId) return;
	const store = useChatStore.getState();
	store.setIsTyping(false);
	store.setMessages([...store.messages, {
		kind: 'error', id: `err_${Date.now()}`, text: data.error ?? 'Unknown error',
	}]);
}

function handlePermissionEvent(chatId: string, data: PermissionRequestData): void {
	if (data.chat_id !== chatId) return;
	useChatStore.getState().addPermission({
		id:         data.approval_id,
		sessionID:  chatId,
		permission: data.tool_name,
		patterns:   data.patterns ?? [],
		metadata:   data.metadata ?? {},
		always:     data.always   ?? [],
	});
}

function handleQuestionEvent(chatId: string, data: QuestionData): void {
	if (data.chat_id !== chatId) return;
	useChatStore.getState().addQuestion({
		id:        data.question_id,
		sessionID: chatId,
		questions: (data.questions ?? []).map(q => ({
			question: q.question,
			header:   q.header,
			options:  (q.options ?? []).map(o => ({ label: o.label, description: o.description })),
			multiple: q.multiple,
		})),
	});
}

// ── History bootstrap → ChatMessage[] ────────────────────────────────────────

function applyHistory(rows: LisMessageSummary[]): void {
	const next: ChatMessage[] = [];
	for (const row of rows) {
		if (row.role === 'tool') {
			if (!row.tool_name && !row.text) continue;
			next.push({
				kind:   'tool', id: row.id,
				name:   row.tool_name ?? 'tool',
				args:   row.tool_args ?? undefined,
				result: row.tool_is_error ? undefined : (row.text ?? undefined),
				error:  row.tool_is_error ? (row.text ?? '(error)') : undefined,
				status: row.tool_is_error ? 'error' : 'done',
				tokens: row.output_tokens ?? undefined,
			});
			continue;
		}
		if (row.role === 'assistant' && row.text?.trim()) {
			if ((row.thinking_tokens ?? 0) > 0 || (row.thinking_content?.length ?? 0) > 0) {
				next.push({
					kind: 'thinking', id: `${row.id}__thinking`,
					content: row.thinking_content ?? '',
					collapsed: true, streaming: false,
					tokens: row.thinking_tokens ?? undefined,
				});
			}
			next.push({
				kind: 'assistant', id: row.id,
				text: row.text ?? '', timestamp: row.timestamp, streaming: false,
				tokens: {
					input_tokens:          row.input_tokens          ?? undefined,
					output_tokens:         row.output_tokens         ?? undefined,
					cache_read_tokens:     row.cache_read_tokens     ?? undefined,
					cache_creation_tokens: row.cache_creation_tokens ?? undefined,
					thinking_tokens:       row.thinking_tokens       ?? undefined,
				},
			});
			continue;
		}
		if (row.role === 'user' && row.text?.trim()) {
			next.push({ kind: 'user', id: row.id, text: row.text ?? '', timestamp: row.timestamp });
		}
	}
	const store = useChatStore.getState();
	store.setIsTyping(false);
	store.setMessages(next);
}

// ── Sessions mirror: global → chat-panel ─────────────────────────────────────

function syncSessions(): void {
	const { activeSessions, archivedSessions } = useGlobalSessionsStore.getState();
	const merged: ChatSummary[] = [
		...activeSessions.map(toChatSummary),
		...archivedSessions.map(toChatSummary),
	];
	useChatStore.getState().setSessions(merged);
}

function toChatSummary(s: {
	id: string;
	title?: string;
	time?: { updated?: number; archived?: number };
	chatDirectory?: string | null;
	projectId?: string | null;
}): ChatSummary {
	const updated  = s.time?.updated  ? new Date(s.time.updated).toISOString()  : new Date(0).toISOString();
	const archived = s.time?.archived ? new Date(s.time.archived).toISOString() : undefined;
	return {
		chat_id:    s.id,
		name:       s.title,
		project_id: s.projectId   ?? null,
		directory:  s.chatDirectory ?? null,
		updated_at: updated,
		archived_at: archived,
	};
}

/**
 * Listen for the host's `init { chatId }` envelope and mount the per-chat data
 * bridge whenever the chatId changes. Sets BOTH the OC-side
 * `useSessionUIStore.currentSessionId` (which gates the send button + drives
 * every per-session selector in the OC chat surface) AND the Lis-side
 * `useChatStore.chatId` (which the bridge writes into for messages/agent
 * state). Returns a teardown that removes the listener and closes the
 * current per-chat bridge.
 */
export function installChatPanelInit(target: Window = window): () => void {
	let currentChatId: string | null = null;
	let stop:          (() => void) | null = null;

	// Debug surface — confirms the listener installed AND tracks every message it sees.
	if (typeof window !== 'undefined') {
		(window as any).__lis_chatPanelInit = {
			installed: Date.now(),
			messageCount: 0,
			lastMessageTypes: [] as string[],
			lastInitPayloads: [] as { chatId: string; isDraft: boolean; project?: string | null; ts: number }[],
			currentChatId: () => currentChatId,
			storeReads: [] as { ts: number; tag: string; currentSessionId: string | null; chatStoreId: string | null }[],
		};
	}

	const handler = (ev: MessageEvent<{
		type?:                   unknown;
		chatId?:                 unknown;
		isDraft?:                unknown;
		draftProjectExternalId?: unknown;
	}>) => {
		const msg = ev.data;
		if (typeof window !== 'undefined') {
			const dbg = (window as any).__lis_chatPanelInit;
			dbg.messageCount += 1;
			const t = String((msg as any)?.type ?? 'no-type');
			dbg.lastMessageTypes.push(t);
			if (dbg.lastMessageTypes.length > 20) dbg.lastMessageTypes.shift();
		}
		const t = (msg as { type?: unknown } | null)?.type;
		if (t === 'init') {
			lisLog.info(`[lis-init-recv] type=init chatId=${(msg as any).chatId} isDraft=${(msg as any).isDraft}`);
			if (typeof window !== 'undefined') {
				const dbg = (window as any).__lis_chatPanelInit;
				dbg.lastInitPayloads.push({
					ts: Date.now(),
					chatId: String((msg as any).chatId ?? ''),
					isDraft: (msg as any).isDraft === true,
					project: typeof (msg as any).draftProjectExternalId === 'string' ? (msg as any).draftProjectExternalId : null,
				});
				if (dbg.lastInitPayloads.length > 10) dbg.lastInitPayloads.shift();
			}
		}
		if (msg?.type !== 'init' || typeof msg.chatId !== 'string' || msg.chatId.length === 0) return;
		if (msg.chatId === currentChatId) return;
		currentChatId = msg.chatId;
		const isDraft = msg.isDraft === true || currentChatId.startsWith('web:draft-');
		const draftProjectExternalId = typeof msg.draftProjectExternalId === 'string' ? msg.draftProjectExternalId : null;
		lisLog.info(`chat-panel: init chatId=${currentChatId} draft=${isDraft} project=${draftProjectExternalId ?? '∅'}`);

		stop?.(); stop = null;

		// OC's ChatInput gates the send button on currentSessionId; without it
		// the user can't send. The bridge's data path uses useChatStore.chatId.
		useSessionUIStore.getState().setCurrentSession(currentChatId, null);
		useChatStore.getState().setChatId(currentChatId);
		// Breadcrumb the immediate post-write reads so we can spot any store
		// reset that comes from elsewhere (race with another bridge mount, etc.).
		if (typeof window !== 'undefined') {
			const dbg = (window as any).__lis_chatPanelInit;
			dbg.storeReads.push({
				ts: Date.now(),
				tag: 'after-setCurrentSession',
				currentSessionId: useSessionUIStore.getState().currentSessionId,
				chatStoreId:      useChatStore.getState().chatId,
			});
			if (dbg.storeReads.length > 10) dbg.storeReads.shift();
		}

		// Draft mode: seed `newSessionDraft` with the project hint so the
		// composer pills render bound to the right project from turn one.
		// `selectedProjectId` is the project's external id (not the FK) —
		// useProjectsStore + the draft pills use the external id as their key.
		if (isDraft) {
			useSessionUIStore.setState({
				newSessionDraft: {
					open:              true,
					selectedProjectId: draftProjectExternalId,
					directoryOverride: null,
				},
			});
		}

		stop = mountChatPanelBridge(currentChatId);
	};

	target.addEventListener('message', handler);
	return () => {
		target.removeEventListener('message', handler);
		stop?.(); stop = null;
	};
}

// ── Re-exports for tests (no other production caller) ────────────────────────

export const __test__ = {
	applyHistory,
	handleStreamEvent,
	syncSessions,
	loadAgents,
	loadChatAgent,
	loadHistory,
};
