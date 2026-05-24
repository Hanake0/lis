import { create } from 'zustand';
export const useSessionMultiSelectStore = create(() => ({ selectedIds: new Set<string>() }));
