import { create } from 'zustand';
const noop: any = () => {};
export const useOpenInAppsStore = create<any>(() => ({
	apps: [], availableApps: [], isLoading: false, isCacheStale: false,
	fetchApps: noop, openInApp: noop, initialize: noop, loadInstalledApps: noop,
}));