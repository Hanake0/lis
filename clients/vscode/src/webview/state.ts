import { signal } from '@preact/signals';
import type { ConnectionState, AgentSummary, ContextSegment } from '../types';

export interface TokenMeta {
	input_tokens?:          number;
	output_tokens?:         number;
	cache_read_tokens?:     number;
	cache_creation_tokens?: number;
	thinking_tokens?:       number;
}

export interface ContextData {
	total:    number;
	budget:   number;
	segments: ContextSegment[];
	model:    string;
	effort?:  string;
}

export type ChatMessage =
	| { kind: 'user';      id: string; text: string; timestamp: string }
	| { kind: 'assistant'; id: string; text: string; timestamp: string; streaming: boolean; tokens?: TokenMeta }
	| { kind: 'thinking';  id: string; content?: string; collapsed: boolean; streaming: boolean; tokens?: number }
	| { kind: 'tool';      id: string; name: string; args?: string; result?: string; error?: string; status: 'running' | 'done' | 'error'; tokens?: number }
	| { kind: 'error';     id: string; text: string };

export const chatId       = signal<string | null>(null);
export const connection   = signal<ConnectionState>('connecting');
export const messages     = signal<ChatMessage[]>([]);
export const agents       = signal<AgentSummary[]>([]);
export const currentAgent = signal<AgentSummary | null>(null);
export const contextData  = signal<ContextData | null>(null);
export const contextOpen  = signal(false);
export const isTyping     = signal(false);

export const composerModel    = signal('');
export const composerEffort   = signal('');
export const composerProfile  = signal('');
export const composerThinking = signal('');
