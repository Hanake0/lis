import { create } from 'zustand';
const noop: any = () => {};
export const useSessionPinnedStore = create<any>(() => ({
	pinnedIds: new Set<string>(),
	ids: new Set<string>(),
	setIds: noop,
	toggle: noop,
}));
