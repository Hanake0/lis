import { create } from 'zustand';
const noop: any = () => {};

interface GlobalSyncStore {
	directories: string[];
}

export const useGlobalSyncStore = create<GlobalSyncStore>(() => ({
	directories: [],
}));
