import { create } from 'zustand';

interface PermissionStoreState {
	autoAcceptSessions: Set<string>;
	setSessionAutoAccept: (sessionId: string, accept: boolean) => void;
	isSessionAutoAccepting: (sessionId: string) => boolean;
}

export const usePermissionStore = create<PermissionStoreState>((set, get) => ({
	autoAcceptSessions: new Set<string>(),
	setSessionAutoAccept: (sessionId: string, accept: boolean) => {
		set((state) => {
			const next = new Set(state.autoAcceptSessions);
			if (accept) next.add(sessionId);
			else next.delete(sessionId);
			return { autoAcceptSessions: next };
		});
	},
	isSessionAutoAccepting: (sessionId: string) => get().autoAcceptSessions.has(sessionId),
}));