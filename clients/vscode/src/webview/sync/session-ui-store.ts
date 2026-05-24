import { create } from 'zustand';
import { useChatStore } from '@/store';
import type { AttachedFile } from '@/stores/types/sessionTypes';

export type { AttachedFile };

const noop: any = () => {};
const asyncNoop: any = async () => {};

export const useSessionUIStore = create<any>((set: any) => ({
	currentSessionId: null as string | null,
	newSessionDraft: null as { open: boolean } | null,
	error: null as string | null,
	isLoading: false,
	abortPromptSessionId: null,
	availableWorktrees: [],
	availableWorktreesByProject: new Map(),
	worktreeMetadata: new Map(),
	isSessionPlanAvailable: false,

	setCurrentSession: (id: string) => set({ currentSessionId: id, newSessionDraft: null }),
	openNewSessionDraft: () => set({ currentSessionId: null, newSessionDraft: { open: true } }),
	closeNewSessionDraft: () => set({ newSessionDraft: null }),
	clearError: () => set({ error: null }),

	createSession: asyncNoop,
	deleteSession: asyncNoop,
	deleteSessions: asyncNoop,
	archiveSession: asyncNoop,
	archiveSessions: asyncNoop,
	updateSessionTitle: asyncNoop,
	shareSession: asyncNoop,
	unshareSession: asyncNoop,
	sendMessage: asyncNoop,
	forkFromMessage: asyncNoop,
	revertToMessage: asyncNoop,
	createSessionFromAssistantMessage: asyncNoop,
	handleSlashRedo: asyncNoop,
	initializeNewOpenChamberSession: asyncNoop,

	acknowledgeSessionAbort: noop,
	clearAbortPrompt: noop,
	setDraftBootstrapPendingDirectory: noop,
	setNewSessionDraftTarget: noop,
	getContextUsage: () => null,
	getDirectoryForSession: () => null,
	getSessionsByDirectory: () => [],
	getWorktreeMetadata: () => null,
	handleSlashUndo: noop,
	setDraftPreserveDirectoryOverride: noop,
	setWorktreeMetadata: noop,
}));

useChatStore.subscribe((state, prev) => {
	if (state.chatId !== prev.chatId && state.chatId) {
		useSessionUIStore.setState({ currentSessionId: state.chatId, newSessionDraft: null });
	}
});
