import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useOpenInAppsStore = create<any>(() => ({
	apps: [], isLoading: false, fetchApps: asyncNoop, openInApp: noop,
}));