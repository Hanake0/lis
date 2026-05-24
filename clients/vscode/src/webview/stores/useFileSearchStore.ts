import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useFileSearchStore = create<any>(() => ({
	results: [], isSearching: false,
}));