import { create } from 'zustand';
const noop: any = () => {};
export const useDesktopSshStore = create<any>(() => ({
	connections: [], hosts: [], instances: [], isConnected: false, isLoading: false,
	currentHost: null, sshKey: null, logs: [], statusesById: {},
	importCandidates: [], isImportsLoading: false, isSaving: false, error: null,
	connect: noop, disconnect: noop, addHost: noop, removeHost: noop,
	setCurrentHost: noop, refreshHosts: noop, clearLogs: noop, fetchLogs: noop,
	getSshStatus: () => null, isHostConnected: () => false, load: noop,
	loadImports: noop, refreshStatuses: noop, upsertInstance: noop,
	createFromCommand: noop, removeInstance: noop, retry: noop,
}));