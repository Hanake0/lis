import { create } from 'zustand';
const noop: any = () => {};

export interface QueuedMessage {
	id: string;
	text: string;
	timestamp: number;
}

interface MessageQueueStore {
	queue: QueuedMessage[];
	addToQueue: (msg: QueuedMessage) => void;
	removeFromQueue: (id: string) => void;
}

export const useMessageQueueStore = create<MessageQueueStore>((set) => ({
	queue: [],
	addToQueue: (msg) => set((s) => ({ queue: [...s.queue, msg] })),
	removeFromQueue: (id) => set((s) => ({ queue: s.queue.filter((m) => m.id !== id) })),
}));
