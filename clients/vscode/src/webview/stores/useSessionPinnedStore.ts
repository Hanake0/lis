import { create } from 'zustand';
export const useSessionPinnedStore = create(() => ({ pinnedIds: new Set<string>() }));
