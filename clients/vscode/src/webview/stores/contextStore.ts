import { create } from 'zustand';

interface ContextStore {
	contextFiles: unknown[];
}

export const useContextStore = create<ContextStore>(() => ({
	contextFiles: [],
}));
