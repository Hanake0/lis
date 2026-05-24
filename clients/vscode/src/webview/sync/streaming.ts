import { create } from 'zustand';
import { useChatStore } from '@/store';

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

useChatStore.subscribe((state) => {
	const sessionId = state.chatId;
	if (!sessionId) return;

	const streamingMsg = state.messages.find((m) => m.kind === 'assistant' && m.streaming);
	const thinkingMsg = state.messages.find((m) => m.kind === 'thinking' && m.streaming);

	const ids = new Map<string, string>();
	const states = new Map<string, MessageStreamState>();

	if (streamingMsg) {
		ids.set(sessionId, streamingMsg.id);
		states.set(streamingMsg.id, { phase: 'text' });
	} else if (thinkingMsg) {
		ids.set(sessionId, thinkingMsg.id);
		states.set(thinkingMsg.id, { phase: 'thinking' });
	} else if (state.isTyping) {
		const lastToolRunning = [...state.messages].reverse().find((m) => m.kind === 'tool' && m.status === 'running');
		if (lastToolRunning) {
			ids.set(sessionId, lastToolRunning.id);
			states.set(lastToolRunning.id, { phase: 'tool' });
		}
	}

	useStreamingStore.setState({ streamingMessageIds: ids, messageStreamStates: states });
});
