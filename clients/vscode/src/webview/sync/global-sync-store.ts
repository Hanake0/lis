import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGlobalSyncStore = create<any>(() => ({
	directories: [], activeDirectories: [],
}));