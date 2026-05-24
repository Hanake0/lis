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
	input_tokens?: number;
	output_tokens?: number;
	cache_read_tokens?: number;
	cache_creation_tokens?: number;
	thinking_tokens?: number;
}

export interface AgentSummary {
	name: string;
	display_name?: string;
	model: string;
	effort?: string;
	tool_profile?: string;
	is_default: boolean;
}

export interface ContextSegment {
	name:   string;
	tokens: number;
}

// ── Server → Client frames ─────────────────────────────────────────

export type ServerFrame =
	| { type: 'connected' }
	| { type: 'message';         chat_id: string; id: string; text: string; role: 'user' | 'assistant'; timestamp: string; input_tokens?: number; output_tokens?: number; cache_read_tokens?: number; cache_creation_tokens?: number; thinking_tokens?: number }
	| { type: 'chunk';           chat_id: string; id: string; delta: string; done: boolean }
	| { type: 'thinking_chunk';  chat_id: string; id: string; delta: string; done: boolean }
	| { type: 'tool_start';      chat_id: string; id: string; tool_name: string; tool_args?: string }
	| { type: 'tool_result';     chat_id: string; id: string; tool_name: string; result?: string; error?: string }
	| { type: 'typing';          chat_id: string }
	| { type: 'typing_stopped';  chat_id: string }
	| { type: 'reaction';        chat_id: string; id: string; emoji: string }
	| { type: 'agent_info';      chat_id: string; agent_name: string; display_name?: string; model: string; effort?: string; tool_profile?: string }
	| { type: 'agent_list';      agents: AgentSummary[] }
	| { type: 'error';           error: string; chat_id?: string }
	| { type: 'chat_list';       chats: ChatSummary[] }
	| { type: 'chat_history';    chat_id: string; messages: MessageRow[] }
	| { type: 'context';         chat_id: string; total: number; budget: number; segments: ContextSegment[]; model: string; effort?: string };

// ── Client → Server frames ─────────────────────────────────────────

export type ClientFrame =
	| { type: 'message';        chat_id: string; text: string; dispatch?: DispatchMode; model?: string; effort?: string; tool_profile?: string; thinking_mode?: string }
	| { type: 'history';        chat_id: string; limit?: number }
	| { type: 'list_chats' }
	| { type: 'set_agent';      chat_id: string; agent_name: string }
	| { type: 'get_agent';      chat_id: string }
	| { type: 'list_agents' }
	| { type: 'save_overrides'; chat_id: string; model?: string; effort?: string; tool_profile?: string }
	| { type: 'get_context';    chat_id: string };
