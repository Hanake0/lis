import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useFilesViewTabsStore = create<any>(() => ({
	tabs: [], activeTabId: null, openTab: noop,
	closeTab: noop, setActiveTab: noop, reorderTabs: noop,
}));