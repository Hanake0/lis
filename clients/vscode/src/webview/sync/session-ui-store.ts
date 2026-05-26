import { create } from 'zustand';
import { useChatStore } from '@/store';
import { LisRpcClient } from '@/lib/lis/rpc-client';
import { lisLog } from '@/lib/lis/logger';
import type { AttachedFile } from '@/stores/types/sessionTypes';

declare global {
	interface Window { __lis_sessionUI?: unknown }
}

export type { AttachedFile };

const noop: any = () => {};
const asyncNoop: any = async () => {};

export const useSessionUIStore = create<any>((rawSet: any) => {
	// Diagnostic: track every write that touches `currentSessionId` so we can
	// pinpoint the caller that's clearing it back to null right before the
	// composer's onClick reads its value. Replaces `set` with a traced wrapper
	// so every action's writes flow through it.
	const set = (updater: any) => rawSet((prev: any) => {
		const partial = typeof updater === 'function' ? updater(prev) : updater;
		if (partial && Object.prototype.hasOwnProperty.call(partial, 'currentSessionId')) {
			const next = partial.currentSessionId;
			if (typeof window !== 'undefined') {
				const w = window as any;
				w.__lis_sessionIdTrace = w.__lis_sessionIdTrace ?? [];
				w.__lis_sessionIdTrace.push({
					ts: Date.now(),
					from: prev.currentSessionId,
					to:   next,
					stack: new Error().stack?.split('\n').slice(1, 8).map((s: string) => s.trim()),
				});
				if (w.__lis_sessionIdTrace.length > 30) w.__lis_sessionIdTrace.shift();
			}
		}
		return partial;
	});
	return ({
	currentSessionId: null as string | null,
	newSessionDraft: null as { open: boolean } | null,
	error: null as string | null,
	isLoading: false,
	abortPromptSessionId: null,
	availableWorktrees: [],
	availableWorktreesByProject: new Map(),
	worktreeMetadata: new Map(),
	isSessionPlanAvailable: false,
	// Per-chat working directory cache. Populated from useChatStore.sessions
	// (see the subscriber below) and from `setSessionDirectory` (composer
	// worktree pill PATCH success). `getDirectoryForSession` reads from this
	// map. null = "inherit project root" — matches the server-side semantic.
	sessionDirectoryMap: new Map<string, string | null>(),
	// Per-chat project external_id cache. Same mirror as
	// `sessionDirectoryMap` — fed by useChatStore.sessions and consumed by
	// `getProjectIdForSession`. Drives the composer's persisted-chat project
	// pill.
	sessionProjectIdMap: new Map<string, string | null>(),

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
		const snap = { content: content?.slice(0, 40), modelId, attachments: attachments?.length ?? 0 };
		lisLog.info('[lis-send] sendMessage called', snap);
		// Window breadcrumb for the e2e test — lisLog routes through postMessage
		// to the extension host's OutputChannel and isn't visible to Playwright.
		try { (window as unknown as { __lis_lastSendMessage?: unknown }).__lis_lastSendMessage = { ts: Date.now(), ...snap }; } catch {}
		const trimmed = (content ?? '').trim();
		const hasAttachments = attachments && attachments.length > 0;
		if (!trimmed && !hasAttachments) {
			lisLog.info('[lis-send] bail: no content and no attachments');
			try { (window as unknown as { __lis_lastSendMessageBail?: unknown }).__lis_lastSendMessageBail = { ts: Date.now(), reason: 'no-content' }; } catch {}
			return;
		}

		// OC's ChatInput is the only caller of this action; it passes the active
		// composer's selections positionally. We read chatId straight from
		// currentSessionId (the OC source of truth) — no Lis-legacy fallback.
		const chatId = useSessionUIStore.getState().currentSessionId as string | null;
		lisLog.info('[lis-send] chatId from store', { chatId });
		if (!chatId) {
			lisLog.info('[lis-send] bail: no chatId');
			return;
		}

		const imageAttachments = hasAttachments
			? attachments
				.filter((f: AttachedFile) => f.mimeType.startsWith('image/') && f.dataUrl)
				.map((f: AttachedFile) => ({ data_url: f.dataUrl, mime_type: f.mimeType }))
			: undefined;

		// 15d draft envelope: on first send of a `web:draft-…` chat, ship the
		// project_id + directory hints so the server's lazy-create path binds
		// the row correctly from turn one. The flag is checked once here — we
		// don't keep "draft" state alive across messages because the server
		// drops the envelope on the second message anyway.
		const draftState = useSessionUIStore.getState().newSessionDraft as {
			open?: boolean; selectedProjectId?: string | null; directoryOverride?: string | null;
		} | null;
		const isDraft = chatId.startsWith('web:draft-');
		const draftProjectId = isDraft ? draftState?.selectedProjectId ?? null : null;
		const draftDirectory = isDraft ? draftState?.directoryOverride ?? null : null;

		// effort / tool_profile / thinking_mode wiring lands in Stage 15c with the
		// composer pills. Today the server falls back to the chat's bound agent's
		// defaults when these are null, which is the correct semantic.
		const postPayload = { chatId, isDraft, modelId, draftProjectId, draftDirectory };
		lisLog.info('[lis-send] POSTing', postPayload);
		try { (window as unknown as { __lis_lastSendMessagePost?: unknown }).__lis_lastSendMessagePost = { ts: Date.now(), ...postPayload }; } catch {}
		try {
			const result = await LisRpcClient.instance().sendChatMessage(chatId, {
				text:          trimmed || null,
				model:         modelId || null,
				effort:        null,
				tool_profile:  null,
				thinking_mode: null,
				attachments:   imageAttachments && imageAttachments.length > 0 ? imageAttachments : undefined,
				project_id:    draftProjectId,
				directory:     draftDirectory,
			});
			lisLog.info('[lis-send] POST returned', result);
		} catch (err) {
			lisLog.error('[lis-send] POST failed', { err: err instanceof Error ? err.message : String(err) });
			throw err;
		}

		// Clear the draft flag — the chat row exists server-side now (the
		// chat_created SSE event will reconcile useGlobalSessionsStore /
		// useChatStore.sessions). The id stays `web:draft-…` until the user
		// closes + reopens the panel, but the composer no longer renders the
		// draft pills (newSessionDraftOpen flips to false).
		if (isDraft) useSessionUIStore.setState({ newSessionDraft: null });
	},
	forkFromMessage: asyncNoop,
	revertToMessage: asyncNoop,
	createSessionFromAssistantMessage: asyncNoop,
	handleSlashRedo: asyncNoop,
	initializeNewOpenChamberSession: asyncNoop,

	acknowledgeSessionAbort: noop,
	clearAbortPrompt: noop,
	setDraftBootstrapPendingDirectory: noop,
	// Composer draft pill writes here when the user changes project or
	// worktree before sending the first message. The fields piggyback on
	// `newSessionDraft` so OC's ChatInput consumes them unchanged (it reads
	// `newSessionDraft.selectedProjectId` / `.directoryOverride`).
	setNewSessionDraftTarget: (
		target: { projectId?: string | null; selectedProjectId?: string | null; directoryOverride?: string | null },
		_options?: { force?: boolean },
	) => set((state: any) => {
		const prev = (state.newSessionDraft ?? {}) as Record<string, unknown>;
		const next: Record<string, unknown> = { ...prev, open: true };
		if ('selectedProjectId' in target) next.selectedProjectId = target.selectedProjectId ?? null;
		if ('projectId'         in target) next.selectedProjectId = target.projectId         ?? next.selectedProjectId ?? null;
		if ('directoryOverride' in target) next.directoryOverride = target.directoryOverride ?? null;
		return { newSessionDraft: next };
	}),
	getContextUsage: () => null,
	getDirectoryForSession: (sessionId: string) => {
		const map = (useSessionUIStore.getState() as { sessionDirectoryMap?: Map<string, string | null> }).sessionDirectoryMap;
		return map?.get(sessionId) ?? null;
	},
	getProjectIdForSession: (sessionId: string) => {
		const map = (useSessionUIStore.getState() as { sessionProjectIdMap?: Map<string, string | null> }).sessionProjectIdMap;
		return map?.get(sessionId) ?? null;
	},
	/**
	 * Rebind a persisted chat's project. POSTs /api/chats/{id}/move which
	 * publishes `chat_updated` via SSE — the sidebar re-buckets the row
	 * automatically. We don't optimistically touch the local map; the
	 * sessions mirror will reconcile when the SSE round-trips.
	 */
	setSessionProject: async (sessionId: string, projectExternalId: string | null) => {
		try {
			await LisRpcClient.instance().moveChat(sessionId, projectExternalId);
		} catch (err) {
			console.warn('[session-ui-store] setSessionProject move failed:', err);
		}
	},
	/**
	 * Rebind a persisted chat's working directory. PATCHes /api/chats/{id}
	 * with `directory` (empty string CLEARS the override). The local cache
	 * is updated optimistically — the next sessions-mirror refresh will
	 * reconcile against the server's authoritative value.
	 */
	setSessionDirectory: async (sessionId: string, directory: string | null) => {
		const trimmed = (directory ?? '').trim();
		const value   = trimmed.length === 0 ? null : trimmed;
		set((state: { sessionDirectoryMap?: Map<string, string | null> }) => {
			const next = new Map(state.sessionDirectoryMap ?? new Map());
			next.set(sessionId, value);
			return { sessionDirectoryMap: next };
		});
		try {
			await LisRpcClient.instance().updateChat(sessionId, { directory: value ?? '' });
		} catch (err) {
			console.warn('[session-ui-store] setSessionDirectory PATCH failed:', err);
		}
	},
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
	});
});

// Debug surface: lets Playwright probe the store state from inside the iframe.
if (typeof window !== 'undefined') {
	(window as any).__lis_sessionUI = useSessionUIStore;
}

// Mirror useChatStore → useSessionUIStore:
//   - contextData → getContextUsage accessor (will retire once
//     contextStore.ts owns the calculation in a future commit)
//   - sessions[].directory → sessionDirectoryMap (drives the composer
//     worktree pill)
// The chatId → currentSessionId mirror moved to
// `chat-panel-bridge.installChatPanelInit` (which already runs on init).
useChatStore.subscribe((state, prev) => {
	if (state.contextData !== prev.contextData) {
		const ctx = state.contextData;
		useSessionUIStore.setState({
			getContextUsage: () => {
				if (!ctx || ctx.budget <= 0) return null;
				return {
					totalTokens:    ctx.total,
					percentage:     Math.round((ctx.total / ctx.budget) * 100),
					contextLimit:   ctx.budget,
					thresholdLimit: ctx.budget,
				};
			},
		});
	}

	if (state.sessions !== prev.sessions) {
		const nextDir = new Map<string, string | null>();
		const nextPrj = new Map<string, string | null>();
		for (const s of state.sessions) {
			nextDir.set(s.chat_id, s.directory  ?? null);
			nextPrj.set(s.chat_id, s.project_id ?? null);
		}
		useSessionUIStore.setState({ sessionDirectoryMap: nextDir, sessionProjectIdMap: nextPrj });
	}
});
