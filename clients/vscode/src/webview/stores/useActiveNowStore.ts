import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useActiveNowStore = create<any>(() => ({
	activeNow: [], activeSessionIds: new Set<string>(),
	isSessionActive: () => false, setActiveNow: noop,
}));