import { create } from 'zustand';
const noop: any = () => {};
export const usePermissionStore = create<any>(() => ({
	permissions: [], setSessionAutoAccept: noop,
	isSessionAutoAccepting: () => false,
	autoAcceptSessions: new Set<string>(),
}));