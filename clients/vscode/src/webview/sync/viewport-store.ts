import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
const EMPTY_MAP: Map<string, any> = new Map();
export const useViewportStore = create<any>((set: any) => ({
	isSyncing: false,
	sessionMemoryState: EMPTY_MAP,
	setIsSyncing: (v: boolean) => set({ isSyncing: v }),
}));