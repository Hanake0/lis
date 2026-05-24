import { create } from 'zustand';

export interface SidebarSession {
	chat_id: string;
	name?: string;
	updated_at?: string;
	archived_at?: string;
}

interface SidebarState {
	connected: boolean;
	sessions: SidebarSession[];
	agents: Array<{ name: string; display_name?: string; model?: string }>;
	activeSessionId: string | null;

	setConnected: (v: boolean) => void;
	setSessions: (s: SidebarSession[]) => void;
	addSession: (s: SidebarSession) => void;
	removeSession: (id: string) => void;
	renameSession: (id: string, name: string) => void;
	archiveSession: (id: string, archivedAt: string) => void;
	setAgents: (a: SidebarState['agents']) => void;
	setActiveSessionId: (id: string | null) => void;
}

export const useSidebarStore = create<SidebarState>((set) => ({
	connected: false,
	sessions: [],
	agents: [],
	activeSessionId: null,

	setConnected: (connected) => set({ connected }),
	setSessions: (sessions) => set({ sessions }),
	addSession: (s) => set((state) => ({
		sessions: state.sessions.some(x => x.chat_id === s.chat_id)
			? state.sessions
			: [s, ...state.sessions],
	})),
	removeSession: (id) => set((state) => ({
		sessions: state.sessions.filter(s => s.chat_id !== id),
	})),
	renameSession: (id, name) => set((state) => ({
		sessions: state.sessions.map(s => s.chat_id === id ? { ...s, name } : s),
	})),
	archiveSession: (id, archivedAt) => set((state) => ({
		sessions: state.sessions.map(s => s.chat_id === id ? { ...s, archived_at: archivedAt } : s),
	})),
	setAgents: (agents) => set({ agents }),
	setActiveSessionId: (activeSessionId) => set({ activeSessionId }),
}));
