import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSessionMultiSelectStore = create<any>(() => ({
	selectedIds: new Set<string>(), ids: new Set<string>(), isActive: false,
	toggle: noop, clear: noop, setIds: noop, selectAll: noop,
}));