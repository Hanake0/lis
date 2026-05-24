import { create } from 'zustand';
const noop: any = () => {};
export const useFileSearchStore = create<any>(() => ({
	results: [], isSearching: false, searchFiles: noop,
}));