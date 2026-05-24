import { useChatStore, type ChatMessage } from '@/store';

export type DirectorySyncState = {
	session: Array<{ id: string; title?: string; parentID?: string; directory?: string }>;
	session_status: Record<string, { type: string; message?: string; confirmedAt?: number } | undefined>;
	permission: Record<string, unknown[]>;
	question: Record<string, unknown[]>;
};

function buildState(): DirectorySyncState {
	const { chatId, isTyping } = useChatStore.getState();
	return {
		session: chatId ? [{ id: chatId, title: 'Chat' }] : [],
		session_status: chatId ? { [chatId]: { type: isTyping ? 'busy' : 'idle' } } : {},
		permission: {},
		question: {},
	};
}

export function useDirectorySync<T>(selector: (state: DirectorySyncState) => T): T {
	const chatId = useChatStore((s) => s.chatId);
	const isTyping = useChatStore((s) => s.isTyping);
	void chatId; void isTyping;
	return selector(buildState());
}

export function useSyncDirectory(): string { return ''; }

export function useSessions(): Array<{ id: string; title?: string; parentID?: string }> {
	const chatId = useChatStore((s) => s.chatId);
	return chatId ? [{ id: chatId, title: 'Chat' }] : [];
}

function convertMessage(m: ChatMessage): { info: any; parts: any[] } {
	if (m.kind === 'user') {
		return {
			info: { id: m.id, role: 'user', time: {} },
			parts: [{ type: 'text', text: m.text }],
		};
	}
	if (m.kind === 'assistant') {
		return {
			info: {
				id: m.id, role: 'assistant',
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
			info: { id: m.id, role: 'assistant', time: { completed: m.streaming ? undefined : Date.now() } },
			parts: [{ type: 'thinking', text: m.content ?? '' }],
		};
	}
	if (m.kind === 'tool') {
		return {
			info: { id: m.id, role: 'assistant', time: { completed: m.status !== 'running' ? Date.now() : undefined } },
			parts: [{
				type: 'tool-invocation',
				toolName: m.name,
				input: m.args,
				output: m.error ?? m.result ?? '',
				state: m.status === 'running' ? 'call' : m.status === 'error' ? 'result' : 'result',
				isError: m.status === 'error',
			}],
		};
	}
	if (m.kind === 'error') {
		return {
			info: { id: m.id, role: 'assistant', time: { completed: Date.now() } },
			parts: [{ type: 'text', text: m.text }],
		};
	}
	return { info: { id: 'unknown', role: 'assistant' }, parts: [] };
}

export function useSessionMessages(_sessionId: string): any[] {
	const messages = useChatStore((s) => s.messages);
	return messages.filter((m) => m.kind === 'user' || m.kind === 'assistant').map((m) => convertMessage(m).info);
}

export function useSessionMessagesResolved(_sessionId: string): boolean { return true; }

export function useSessionMessageCount(_sessionId: string): number {
	return useChatStore((s) => s.messages.length);
}

export function useSessionMessageRecords(_sessionId: string): Array<{ info: any; parts: any[] }> {
	const messages = useChatStore((s) => s.messages);
	return messages.map(convertMessage);
}

export function useSessionStatus(_sessionId: string): { type: string } | null {
	const isTyping = useChatStore((s) => s.isTyping);
	const hasStreaming = useChatStore((s) => s.messages.some((m) => m.kind === 'assistant' && m.streaming));
	return (isTyping || hasStreaming) ? { type: 'busy' } : { type: 'idle' };
}

export function useUserMessageHistory(): unknown[] { return []; }

export function setActiveSession(_sessionId: string): void {}

export class SyncProvider {
	static defaultProps = {};
}

export function buildSessionMessageRecordsSnapshot(..._args: any[]): any { return null; }
export function useAllLiveSessions(..._args: any[]): any { return []; }
export function useAllSessionStatuses(..._args: any[]): any { return {}; }
export function useDirectoryStore(..._args: any[]): any { return null; }
export function useEnsureSessionMessages(..._args: any[]): any { return null; }
export function useGlobalSessionStatus(..._args: any[]): any { return null; }
export function useSession(..._args: any[]): any { return null; }
export function useSessionPermissions(..._args: any[]): any { return []; }
