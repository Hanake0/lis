import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useMcpStore = create<any>(() => ({
	servers: [], tools: [], isLoading: false, error: null,
	fetchServers: asyncNoop, fetchTools: asyncNoop, installServer: asyncNoop, removeServer: asyncNoop,
}));
export function computeMcpHealth(..._a: any[]): any { return null; }