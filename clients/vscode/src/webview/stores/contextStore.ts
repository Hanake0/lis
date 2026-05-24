import { create } from 'zustand';
export const useContextStore = create<any>(() => ({
	contextFiles: [], hasHydrated: true,
}));