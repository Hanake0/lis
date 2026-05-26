import { useMemo } from 'react';
import { useChatStore, type ChatMessage } from '@/store';

export type DirectorySyncState = {
	session: Array<{ id: string; title?: string; parentID?: string; directory?: string }>;
	session_status: Record<string, { type: string; message?: string; confirmedAt?: number } | undefined>;
	permission: Record<string, unknown[]>;
	question: Record<string, unknown[]>;
	message: Record<string, unknown[]>;
};

const EMPTY_MESSAGE: Record<string, unknown[]> = {};

export function useDirectorySync<T>(selector: (state: DirectorySyncState) => T): T {
	const chatId      = useChatStore((s) => s.chatId);
	const sessions    = useChatStore((s) => s.sessions);
	const isTyping    = useChatStore((s) => s.isTyping);
	const permissions = useChatStore((s) => s.permissions);
	const questions   = useChatStore((s) => s.questions);

	const state = useMemo<DirectorySyncState>(() => ({
		session: sessions.length > 0
			? sessions.map(s => ({ id: s.chat_id, title: s.name }))
			: chatId ? [{ id: chatId, title: 'Chat' }] : [],
		session_status: chatId ? { [chatId]: { type: isTyping ? 'busy' : 'idle' } } : {},
		permission: chatId && permissions.length > 0 ? { [chatId]: permissions } : {},
		question: chatId && questions.length > 0 ? { [chatId]: questions } : {},
		message: EMPTY_MESSAGE,
		todo: {},
	}), [chatId, sessions, isTyping, permissions, questions]);
	return selector(state);
}

export function useSyncDirectory(): string { return ''; }

export function useSessions(): Array<{ id: string; title?: string; parentID?: string; time?: { created?: number; updated?: number; archived?: number } }> {
	const sessions = useChatStore((s) => s.sessions);
	const chatId   = useChatStore((s) => s.chatId);
	return useMemo(() => {
		if (sessions.length === 0) {
			return chatId ? [{ id: chatId, title: 'Chat' }] : [];
		}
		return sessions.map(s => ({
			id:    s.chat_id,
			title: s.name,
			time: {
				updated: s.updated_at ? new Date(s.updated_at).getTime() : undefined,
				archived: s.archived_at ? new Date(s.archived_at).getTime() : undefined,
			},
		}));
	}, [sessions, chatId]);
}

function convertMessages(messages: ChatMessage[]): Array<{ info: any; parts: any[] }> {
	let lastUserID: string | undefined;
	return messages.map((m) => {
		if (m.kind === 'user') {
			lastUserID = m.id;
			return {
				info: { id: m.id, role: 'user', time: {} },
				parts: [{ type: 'text', id: `${m.id}:text`, text: m.text }],
			};
		}
		const parentID = lastUserID;
		if (m.kind === 'assistant') {
			return {
				info: {
					id: m.id, role: 'assistant', parentID,
					time: { completed: m.streaming ? undefined : Date.now() },
					tokens: m.tokens ? {
						input: m.tokens.input_tokens ?? 0,
						output: m.tokens.output_tokens ?? 0,
						reasoning: m.tokens.thinking_tokens ?? 0,
						cache: { read: m.tokens.cache_read_tokens ?? 0, write: m.tokens.cache_creation_tokens ?? 0 },
					} : undefined,
				},
				parts: [{ type: 'text', id: `${m.id}:text`, text: m.text }],
			};
		}
		if (m.kind === 'thinking') {
			// OC's parts vocabulary uses `reasoning` for chain-of-thought text;
			// `thinking` is an LLM-side concept, not a renderable part type.
			// ReasoningPart + AssistantTextPart both branch on type === 'reasoning'.
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: m.streaming ? undefined : Date.now() } },
				parts: [{ type: 'reasoning', id: `${m.id}:reasoning`, text: (m as any).content ?? '' }],
			};
		}
		if (m.kind === 'tool') {
			// OC's ToolPart shape — `type: 'tool'`, named `tool`, `state` is an
			// object keyed by status. Reading our chat-stream messages with the
			// old `tool-invocation` shape silently dropped them — ToolPart's
			// gather loop only processes `part.type === 'tool'`.
			//
			// MessageBody.isToolFinalized requires state.time.end to be a number;
			// without it, finalized tools are filtered out by shouldShowTool and
			// never render. We stamp time.{start,end} from now() because our
			// internal ChatMessage doesn't carry per-event timestamps yet.
			const isDone   = m.status !== 'running';
			const isError  = m.status === 'error';
			const status   = isError ? 'error' : isDone ? 'completed' : 'running';
			let parsedInput: unknown = m.args;
			if (typeof m.args === 'string') {
				try { parsedInput = JSON.parse(m.args); } catch { parsedInput = m.args; }
			}
			const now = Date.now();
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: isDone ? now : undefined } },
				parts: [{
					type:  'tool',
					id:    m.id,
					tool:  m.name,
					state: {
						status,
						input:  parsedInput,
						output: m.result ?? undefined,
						error:  m.error  ?? undefined,
						title:  m.name,
						time:   { start: now, end: isDone ? now : undefined },
					},
				}],
			};
		}
		if (m.kind === 'error') {
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: Date.now() } },
				parts: [{ type: 'text', id: `${m.id}:text`, text: m.text }],
			};
		}
		return { info: { id: 'unknown', role: 'assistant', parentID }, parts: [] };
	});
}

export function useSessionMessages(_sessionId: string): any[] {
	const messages = useChatStore((s) => s.messages);
	return useMemo(
		() => convertMessages(messages).filter((r) => r.info.role === 'user' || r.info.role === 'assistant').map((r) => r.info),
		[messages],
	);
}

export function useSessionMessagesResolved(_sessionId: string): boolean { return true; }

export function useSessionMessageCount(_sessionId: string): number {
	return useChatStore((s) => s.messages.length);
}

export function useSessionMessageRecords(_sessionId: string): Array<{ info: any; parts: any[] }> {
	const messages = useChatStore((s) => s.messages);
	return useMemo(() => convertMessages(messages), [messages]);
}

const BUSY_STATUS = { type: 'busy' as const };
const IDLE_STATUS = { type: 'idle' as const };

export function useSessionStatus(_sessionId: string): { type: string } | null {
	const isTyping = useChatStore((s) => s.isTyping);
	const hasStreaming = useChatStore((s) => s.messages.some((m) => m.kind === 'assistant' && (m as any).streaming));
	return (isTyping || hasStreaming) ? BUSY_STATUS : IDLE_STATUS;
}

const EMPTY_HISTORY: unknown[] = [];
const EMPTY_SESSIONS: any[] = [];
const EMPTY_STATUSES: Record<string, never> = {};
const EMPTY_PERMS: any[] = [];

export function useUserMessageHistory(): unknown[] { return EMPTY_HISTORY; }

export function setActiveSession(_sessionId: string): void {}

export class SyncProvider {
	static defaultProps = {};
}

export function buildSessionMessageRecordsSnapshot(..._args: any[]): any { return EMPTY_SESSIONS; }

export function useAllLiveSessions(): any[] {
	const sessions = useChatStore((s) => s.sessions);
	return useMemo(() => {
		if (sessions.length === 0) return EMPTY_SESSIONS;
		return sessions.map(s => ({
			id: s.chat_id,
			title: s.name,
			directory: '/lis',
			projectID: 'lis',
			time: {
				updated: s.updated_at ? new Date(s.updated_at).getTime() : undefined,
				archived: s.archived_at ? new Date(s.archived_at).getTime() : undefined,
			},
		}));
	}, [sessions]);
}

export function useAllSessionStatuses(): any { return EMPTY_STATUSES; }
export function useDirectoryStore(..._args: any[]): any { return null; }
export function useEnsureSessionMessages(..._args: any[]): any { return null; }
export function useGlobalSessionStatus(..._args: any[]): any { return null; }
export function useSession(..._args: any[]): any { return null; }
export function useSessionPermissions(..._args: any[]): any[] { return EMPTY_PERMS; }
