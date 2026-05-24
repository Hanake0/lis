import { create } from 'zustand';
const noop: any = () => {};
export const useFilesViewTabsStore = create<any>(() => ({
	tabs: [], activeTabId: null, selectedPath: null, openPaths: [], expandedPaths: new Set<string>(),
	openTab: noop, closeTab: noop, setActiveTab: noop, reorderTabs: noop,
	setSelectedPath: noop, addOpenPath: noop, removeOpenPathsByPrefix: noop,
	toggleExpandedPath: noop, removeOpenPath: noop, expandPaths: noop,
	byRoot: {},
}));