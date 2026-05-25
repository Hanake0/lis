import { create } from 'zustand';
import { LisRpcClient } from '@/lib/lis/rpc-client';
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
		await LisRpcClient.instance().createChat({ chatId, name: title ?? 'New Chat' });
		return chatId;
	},
	deleteSession: async (sessionId: string) => {
		await LisRpcClient.instance().deleteChat(sessionId);
		return true;
	},
	deleteSessions: async (ids: string[]) => {
		const client = LisRpcClient.instance();
		const deleted: string[] = [];
		const failed:  string[] = [];
		// Sequential — backend writes a row per call and emits a `chat_deleted` event;
		// running them serially keeps the SSE stream ordered and avoids per-id race conditions.
		for (const id of ids) {
			try { await client.deleteChat(id); deleted.push(id); }
			catch { failed.push(id); }
		}
		return { deletedIds: deleted, failedIds: failed };
	},
	archiveSession: async (sessionId: string) => {
		await LisRpcClient.instance().archiveChat(sessionId);
		return true;
	},
	archiveSessions: async (ids: string[]) => {
		const client = LisRpcClient.instance();
		const archived: string[] = [];
		const failed:   string[] = [];
		for (const id of ids) {
			try { await client.archiveChat(id); archived.push(id); }
			catch { failed.push(id); }
		}
		return { archivedIds: archived, failedIds: failed };
	},
	updateSessionTitle: async (sessionId: string, title: string) => {
		await LisRpcClient.instance().updateChat(sessionId, { name: title });
	},
	shareSession: asyncNoop,
	unshareSession: asyncNoop,
	sendMessage: async (
		content:     string,
		_providerId: string,
		modelId:     string,
		_agent?:     string,
		attachments?: AttachedFile[],
	) => {
		const trimmed = (content ?? '').trim();
		const hasAttachments = attachments && attachments.length > 0;
		if (!trimmed && !hasAttachments) return;

		// OC's ChatInput is the only caller of this action; it passes the active
		// composer's selections positionally. We read chatId straight from
		// currentSessionId (the OC source of truth) — no Lis-legacy fallback.
		const chatId = useSessionUIStore.getState().currentSessionId as string | null;
		if (!chatId) return;

		const imageAttachments = hasAttachments
			? attachments
				.filter((f: AttachedFile) => f.mimeType.startsWith('image/') && f.dataUrl)
				.map((f: AttachedFile) => ({ data_url: f.dataUrl, mime_type: f.mimeType }))
			: undefined;

		// effort / tool_profile / thinking_mode wiring lands in Stage 15c with the
		// composer pills. Today the server falls back to the chat's bound agent's
		// defaults when these are null, which is the correct semantic.
		await LisRpcClient.instance().sendChatMessage(chatId, {
			text:          trimmed || null,
			model:         modelId || null,
			effort:        null,
			tool_profile:  null,
			thinking_mode: null,
			attachments:   imageAttachments && imageAttachments.length > 0 ? imageAttachments : undefined,
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
