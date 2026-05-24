import { create } from 'zustand';
const noop: any = () => {};
export const useSessionDisplayStore = create<any>(() => ({
	displaySettings: {}, displayMode: 'list', showRecentSection: true,
	collapsedSections: new Set<string>(), sessionSearchQuery: '',
	setSessionSearchQuery: noop, toggleSection: noop,
	isSectionCollapsed: () => false, setDisplayMode: noop, toggleRecentSection: noop,
}));