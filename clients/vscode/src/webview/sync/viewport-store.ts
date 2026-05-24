import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useViewportStore = create<any>((set: any) => ({
	isSyncing: false,
	setIsSyncing: (v: boolean) => set({ isSyncing: v }),
}));