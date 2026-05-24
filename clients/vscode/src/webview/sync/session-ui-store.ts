import { create } from 'zustand';
import type { AttachedFile } from '@/stores/types/sessionTypes';

export type { AttachedFile };

interface NewSessionDraft {
	open: boolean;
}

interface SessionUIStore {
	currentSessionId: string | null;
	newSessionDraft: NewSessionDraft | null;
	error: string | null;
	setCurrentSession: (id: string, directory?: string | null) => void;
	openNewSessionDraft: () => void;
	closeNewSessionDraft: () => void;
	clearError: () => void;
}

export const useSessionUIStore = create<SessionUIStore>((set) => ({
	currentSessionId: null,
	newSessionDraft: null,
	error: null,
	setCurrentSession: (id) => set({ currentSessionId: id, newSessionDraft: null }),
	openNewSessionDraft: () => set({ currentSessionId: null, newSessionDraft: { open: true } }),
	closeNewSessionDraft: () => set({ newSessionDraft: null }),
	clearError: () => set({ error: null }),
}));
