import { create } from 'zustand';

interface GlobalSyncStore {
	directories: string[];
}

export const useGlobalSyncStore = create<GlobalSyncStore>(() => ({
	directories: [],
}));
