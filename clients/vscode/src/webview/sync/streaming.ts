import { create } from 'zustand';

export type StreamPhase = 'thinking' | 'text' | 'tool';

interface MessageStreamState {
	phase: StreamPhase | null;
}

interface StreamingStore {
	streamingMessageIds: Map<string, string>;
	messageStreamStates: Map<string, MessageStreamState>;
}

export const useStreamingStore = create<StreamingStore>(() => ({
	streamingMessageIds: new Map(),
	messageStreamStates: new Map(),
}));
