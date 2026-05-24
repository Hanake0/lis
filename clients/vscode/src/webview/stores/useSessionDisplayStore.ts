import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSessionDisplayStore = create<any>(() => ({
	displaySettings: {}, collapsedSections: new Set<string>(),
	sessionSearchQuery: '', setSessionSearchQuery: noop,
	toggleSection: noop, isSectionCollapsed: () => false,
}));