import { create } from 'zustand';

interface UpdateStore {
	checkForUpdates: () => Promise<number | undefined>;
}

export const useUpdateStore = create<UpdateStore>(() => ({
	checkForUpdates: async () => undefined,
}));
