import { create } from 'zustand';
const noop: any = () => {};
export const useSessionMultiSelectStore = create(() => ({ selectedIds: new Set<string>() }));
