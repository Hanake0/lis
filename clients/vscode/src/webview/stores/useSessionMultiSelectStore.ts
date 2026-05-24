import { create } from 'zustand';
const noop: any = () => {};
export const useSessionMultiSelectStore = create<any>(() => ({
	selectedIds: new Set<string>(), ids: new Set<string>(),
	isActive: false, enabled: false, scopeKey: null,
	toggle: noop, clear: noop, setIds: noop, selectAll: noop,
	toggleSelected: noop, setRange: noop,
}));