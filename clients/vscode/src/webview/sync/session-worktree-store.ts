import { create } from 'zustand';

interface SessionWorktreeStore {
	attachments: Map<string, unknown>;
}

export const useSessionWorktreeStore = create<SessionWorktreeStore>(() => ({
	attachments: new Map(),
}));
