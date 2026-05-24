import { create } from 'zustand';
const noop: any = () => {};
export const useGlobalSyncStore = create<any>(() => ({
	directories: [], activeDirectories: [], error: null,
}));