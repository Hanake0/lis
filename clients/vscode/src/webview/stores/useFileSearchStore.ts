import { create } from 'zustand';
export const useFileSearchStore = create(() => ({ results: [], isSearching: false }));
