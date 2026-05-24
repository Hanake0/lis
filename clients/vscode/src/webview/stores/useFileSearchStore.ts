import { create } from 'zustand';
const noop: any = () => {};
export const useFileSearchStore = create(() => ({ results: [], isSearching: false }));
