import { create } from 'zustand';
const noop: any = () => {};
export const useSessionPinnedStore = create(() => ({ pinnedIds: new Set<string>() }));
