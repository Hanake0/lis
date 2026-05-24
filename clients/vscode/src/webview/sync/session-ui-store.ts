import { create } from 'zustand';
import { useChatStore } from '@/store';
import { vscode } from '@/vscodeApi';
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

	createSession: async (title?: string) => {
		const chatId = `web:${crypto.randomUUID()}`;
		vscode.postMessage({ type: 'create_session', chat_id: chatId, name: title ?? 'New Chat' });
		return chatId;
	},
	deleteSession: async (sessionId: string) => {
		vscode.postMessage({ type: 'delete_session', chat_id: sessionId });
		return true;
	},
	deleteSessions: async (ids: string[]) => {
		for (const id of ids) vscode.postMessage({ type: 'delete_session', chat_id: id });
		return { deletedIds: ids, failedIds: [] as string[] };
	},
	archiveSession: async (sessionId: string) => {
		vscode.postMessage({ type: 'archive_session', chat_id: sessionId });
		return true;
	},
	archiveSessions: async (ids: string[]) => {
		for (const id of ids) vscode.postMessage({ type: 'archive_session', chat_id: id });
		return { archivedIds: ids, failedIds: [] as string[] };
	},
	updateSessionTitle: async (sessionId: string, title: string) => {
		vscode.postMessage({ type: 'rename_session', chat_id: sessionId, name: title });
	},
	shareSession: asyncNoop,
	unshareSession: asyncNoop,
	sendMessage: async (text: string, _providerId?: string, _modelId?: string, _agentName?: string, attachments?: AttachedFile[]) => {
		const trimmed = (text ?? '').trim();
		const hasAttachments = attachments && attachments.length > 0;
		if (!trimmed && !hasAttachments) return;

		const { composerModel, composerEffort, composerProfile, composerThinking } = useChatStore.getState();

		const imageAttachments = hasAttachments
			? attachments
				.filter((f: AttachedFile) => f.mimeType.startsWith('image/') && f.dataUrl)
				.map((f: AttachedFile) => ({ filename: f.filename, mime_type: f.mimeType, data_url: f.dataUrl }))
			: undefined;

		vscode.postMessage({
			type: 'send',
			text: trimmed || undefined,
			model: composerModel || undefined,
			effort: composerEffort || undefined,
			tool_profile: composerProfile || undefined,
			thinking_mode: composerThinking || undefined,
			attachments: imageAttachments && imageAttachments.length > 0 ? imageAttachments : undefined,
		});
	},
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
	getWorktreeMetadata: (sessionId: string) => {
		const map = (useSessionUIStore.getState() as { worktreeMetadata?: Map<string, unknown> }).worktreeMetadata;
		return map?.get(sessionId) ?? null;
	},
	handleSlashUndo: noop,
	setDraftPreserveDirectoryOverride: noop,
	// Real storage so multi-run + worktree-aware flows can recover the path the chat was opened in.
	// OC originally backs this with a Map too; the byte-identical components consume it via
	// `getWorktreeMetadata(sessionId)`.
	setWorktreeMetadata: (sessionId: string, metadata: unknown) => set((state: { worktreeMetadata?: Map<string, unknown> }) => {
		const next = new Map(state.worktreeMetadata ?? new Map());
		if (metadata === null || metadata === undefined) next.delete(sessionId);
		else                                             next.set   (sessionId, metadata);
		return { worktreeMetadata: next };
	}),
}));

useChatStore.subscribe((state, prev) => {
	if (state.chatId !== prev.chatId && state.chatId) {
		useSessionUIStore.setState({ currentSessionId: state.chatId, newSessionDraft: null });
	}

	const ctx = state.contextData;
	const prevCtx = prev.contextData;
	if (ctx !== prevCtx) {
		useSessionUIStore.setState({
			getContextUsage: () => {
				if (!ctx || ctx.budget <= 0) return null;
				return {
					totalTokens: ctx.total,
					percentage: Math.round((ctx.total / ctx.budget) * 100),
					contextLimit: ctx.budget,
					thresholdLimit: ctx.budget,
				};
			},
		});
	}
});
