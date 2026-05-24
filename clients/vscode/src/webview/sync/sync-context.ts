import { useMemo } from 'react';
import { useChatStore, type ChatMessage } from '@/store';

export type DirectorySyncState = {
	session: Array<{ id: string; title?: string; parentID?: string; directory?: string }>;
	session_status: Record<string, { type: string; message?: string; confirmedAt?: number } | undefined>;
	permission: Record<string, unknown[]>;
	question: Record<string, unknown[]>;
	message: Record<string, unknown[]>;
};

const EMPTY_PERMISSION: Record<string, unknown[]> = {};
const EMPTY_QUESTION: Record<string, unknown[]> = {};
const EMPTY_MESSAGE: Record<string, unknown[]> = {};

export function useDirectorySync<T>(selector: (state: DirectorySyncState) => T): T {
	const chatId = useChatStore((s) => s.chatId);
	const isTyping = useChatStore((s) => s.isTyping);
	const state = useMemo<DirectorySyncState>(() => ({
		session: chatId ? [{ id: chatId, title: 'Chat' }] : [],
		session_status: chatId ? { [chatId]: { type: isTyping ? 'busy' : 'idle' } } : {},
		permission: EMPTY_PERMISSION,
		question: EMPTY_QUESTION,
		message: EMPTY_MESSAGE,
		todo: {},
	}), [chatId, isTyping]);
	return selector(state);
}

export function useSyncDirectory(): string { return ''; }

export function useSessions(): Array<{ id: string; title?: string; parentID?: string }> {
	const chatId = useChatStore((s) => s.chatId);
	return useMemo(() => chatId ? [{ id: chatId, title: 'Chat' }] : [], [chatId]);
}

function convertMessages(messages: ChatMessage[]): Array<{ info: any; parts: any[] }> {
	let lastUserID: string | undefined;
	return messages.map((m) => {
		if (m.kind === 'user') {
			lastUserID = m.id;
			return {
				info: { id: m.id, role: 'user', time: {} },
				parts: [{ type: 'text', text: m.text }],
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
				parts: [{ type: 'text', text: m.text }],
			};
		}
		if (m.kind === 'thinking') {
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: m.streaming ? undefined : Date.now() } },
				parts: [{ type: 'thinking', text: (m as any).content ?? '' }],
			};
		}
		if (m.kind === 'tool') {
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: m.status !== 'running' ? Date.now() : undefined } },
				parts: [{
					type: 'tool-invocation',
					toolName: m.name,
					input: m.args,
					output: m.error ?? m.result ?? '',
					state: m.status === 'running' ? 'call' : 'result',
					isError: m.status === 'error',
				}],
			};
		}
		if (m.kind === 'error') {
			return {
				info: { id: m.id, role: 'assistant', parentID, time: { completed: Date.now() } },
				parts: [{ type: 'text', text: m.text }],
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
export function useAllLiveSessions(): any[] { return EMPTY_SESSIONS; }
export function useAllSessionStatuses(): any { return EMPTY_STATUSES; }
export function useDirectoryStore(..._args: any[]): any { return null; }
export function useEnsureSessionMessages(..._args: any[]): any { return null; }
export function useGlobalSessionStatus(..._args: any[]): any { return null; }
export function useSession(..._args: any[]): any { return null; }
export function useSessionPermissions(..._args: any[]): any[] { return EMPTY_PERMS; }
