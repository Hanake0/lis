import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export interface QueuedMessage { id: string; text: string; timestamp: number; }
export const useMessageQueueStore = create<any>((set: any) => ({
	queue: [] as QueuedMessage[],
	pendingQueue: [] as QueuedMessage[],
	addToQueue: (msg: QueuedMessage) => set((s: any) => ({ queue: [...s.queue, msg] })),
	removeFromQueue: (id: string) => set((s: any) => ({ queue: s.queue.filter((m: any) => m.id !== id) })),
	clearQueue: () => set({ queue: [] }),
	queueModeEnabled: false, popToInput: noop, pendingQueue: [],
}));