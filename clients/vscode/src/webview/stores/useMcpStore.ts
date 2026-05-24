import { create } from 'zustand';
const noop: any = () => {};
export const useMcpStore = create<any>(() => ({
	servers: [], tools: [], isLoading: false, error: null,
	fetchServers: noop, fetchTools: noop, installServer: noop, removeServer: noop,
	refresh: noop, connect: noop, disconnect: noop, completeAuth: noop,
	startAuth: noop, clearAuth: noop, testConnection: noop, getErrorForDirectory: () => null,
	getStatusForDirectory: () => null,
}));
export function computeMcpHealth(): any { return null; }