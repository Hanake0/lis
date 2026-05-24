import { create } from 'zustand';

interface ViewportStore {
	isSyncing: boolean;
	setIsSyncing: (v: boolean) => void;
}

export const useViewportStore = create<ViewportStore>((set) => ({
	isSyncing: false,
	setIsSyncing: (v) => set({ isSyncing: v }),
}));
