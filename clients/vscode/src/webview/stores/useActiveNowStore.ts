import { create } from 'zustand';
const noop: any = () => {};
export const useActiveNowStore = create<any>(() => ({
	activeNow: [], activeSessionIds: new Set<string>(), entries: [],
	isSessionActive: () => false, setActiveNow: noop, addSession: noop, prune: noop,
}));