import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useDesktopSshStore = create<any>(() => ({
	connections: [], hosts: [], isConnected: false, isLoading: false,
	currentHost: null, sshKey: null, logs: [],
	connect: asyncNoop, disconnect: asyncNoop, addHost: noop, removeHost: noop,
	setCurrentHost: noop, refreshHosts: asyncNoop, clearLogs: noop,
	fetchLogs: asyncNoop, getSshStatus: () => null, isHostConnected: () => false,
}));