// Shared type definitions for the Lis extension and webview.

export type ConnectionState = 'disconnected' | 'connecting' | 'connected';

export type DispatchMode = 'default' | 'immediate' | 'queue_only' | 'interrupt';

// ── DTOs ────────────────────────────────────────────────────────────

export interface ChatSummary {
	chat_id: string;
	name?: string;
	updated_at: string;
}

export interface MessageRow {
	id: string;
	role: string;
	text?: string;
	timestamp: string;
}

// ── Server → Client frames ─────────────────────────────────────────

export type ServerFrame =
	| { type: 'connected' }
	| { type: 'message';        chat_id: string; id: string; text: string; role: 'user' | 'assistant'; timestamp: string }
	| { type: 'typing';         chat_id: string }
	| { type: 'typing_stopped'; chat_id: string }
	| { type: 'reaction';       chat_id: string; id: string; emoji: string }
	| { type: 'error';          error: string; chat_id?: string }
	| { type: 'chat_list';      chats: ChatSummary[] }
	| { type: 'chat_history';   chat_id: string; messages: MessageRow[] };

// ── Client → Server frames ─────────────────────────────────────────

export type ClientFrame =
	| { type: 'message';    chat_id: string; text: string; dispatch?: DispatchMode }
	| { type: 'history';    chat_id: string; limit?: number }
	| { type: 'list_chats' };
