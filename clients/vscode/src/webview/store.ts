import { create } from 'zustand';
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

interface ChatStore {
	chatId:           string | null;
	connection:       ConnectionState;
	messages:         ChatMessage[];
	agents:           AgentSummary[];
	currentAgent:     AgentSummary | null;
	contextData:      ContextData | null;
	contextOpen:      boolean;
	isTyping:         boolean;
	composerModel:    string;
	composerEffort:   string;
	composerProfile:  string;
	composerThinking: string;

	setChatId:           (id: string) => void;
	setConnection:       (s: ConnectionState) => void;
	setMessages:         (msgs: ChatMessage[]) => void;
	setAgents:           (a: AgentSummary[]) => void;
	setCurrentAgent:     (a: AgentSummary | null) => void;
	setContextData:      (d: ContextData | null) => void;
	setContextOpen:      (v: boolean) => void;
	setIsTyping:         (v: boolean) => void;
	setComposerModel:    (v: string) => void;
	setComposerEffort:   (v: string) => void;
	setComposerProfile:  (v: string) => void;
	setComposerThinking: (v: string) => void;
}

export const useChatStore = create<ChatStore>((set) => ({
	chatId:           null,
	connection:       'connecting',
	messages:         [],
	agents:           [],
	currentAgent:     null,
	contextData:      null,
	contextOpen:      false,
	isTyping:         false,
	composerModel:    '',
	composerEffort:   '',
	composerProfile:  '',
	composerThinking: '',

	setChatId:           (chatId)           => set({ chatId }),
	setConnection:       (connection)       => set({ connection }),
	setMessages:         (messages)         => set({ messages }),
	setAgents:           (agents)           => set({ agents }),
	setCurrentAgent:     (currentAgent)     => set({ currentAgent }),
	setContextData:      (contextData)      => set({ contextData }),
	setContextOpen:      (contextOpen)      => set({ contextOpen }),
	setIsTyping:         (isTyping)         => set({ isTyping }),
	setComposerModel:    (composerModel)    => set({ composerModel }),
	setComposerEffort:   (composerEffort)   => set({ composerEffort }),
	setComposerProfile:  (composerProfile)  => set({ composerProfile }),
	setComposerThinking: (composerThinking) => set({ composerThinking }),
}));
