import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native session-folders store. Folders are persisted on the backend via
 * `/api/session-folders/*` (Stage 4) and pushed cross-client via the
 * `folder_created` / `folder_updated` / `folder_deleted` SSE events.
 *
 * The public surface mirrors OC's `useSessionFoldersStore` so the byte-identical
 * `components/session/` files compile unchanged. The only difference is async
 * persistence under the hood:
 *
 *  - Reads (`getFoldersForScope`) are pure selectors. They opportunistically
 *    trigger a one-shot `loadScope` on first call per scope-key so the data
 *    arrives on the next render without consumers needing to wire an effect.
 *  - Mutations (`createFolder`/`renameFolder`/…) are optimistic: local state
 *    flips first, then the REST call fires in the background. Failure rolls
 *    back; success is idempotent against the SSE replay (so we don't double-
 *    apply the same change when our own write echoes back).
 *  - `createFolder` returns a `SessionFolder` synchronously — same as OC — by
 *    minting a local id. When the POST resolves with the canonical server id
 *    we swap the local id for the server one across the folder map.
 */

// ── Wire types (mirror Lis.Api.Schemas.Folders.SessionFolderDto) ─────────────

interface SessionFolderItemDto {
	chat_id:    string;
	sort_index: number;
	added_at:   string;
}

export interface SessionFolderDto {
	id:         string;
	scope_key:  string;
	name:       string;
	parent_id:  string | null;
	sort_index: number;
	items:      SessionFolderItemDto[];
	created_at: string;
	updated_at: string;
}

// ── Public store surface (OC-compatible) ────────────────────────────────────

export interface SessionFolder {
	id:          string;
	name:        string;
	sessionIds:  string[];
	createdAt:   number;
	parentId?:   string | null;
}

export type SessionFoldersMap = Record<string, SessionFolder[]>;

interface SessionFoldersStore {
	foldersMap:         SessionFoldersMap;
	collapsedFolderIds: Set<string>;
	scopesLoaded:       Set<string>;
	loading:            boolean;
	lastError:          string | null;

	loadScope:                 (scopeKey: string) => Promise<void>;
	getFoldersForScope:        (scopeKey: string) => SessionFolder[];
	createFolder:              (scopeKey: string, name: string, parentId?: string | null) => SessionFolder;
	renameFolder:              (scopeKey: string, folderId: string, name: string) => void;
	deleteFolder:              (scopeKey: string, folderId: string) => void;
	addSessionToFolder:        (scopeKey: string, folderId: string, sessionId: string) => void;
	addSessionsToFolder:       (scopeKey: string, folderId: string, sessionIds: string[]) => void;
	removeSessionFromFolder:   (scopeKey: string, sessionId: string) => void;
	removeSessionsFromFolders: (scopeKey: string, sessionIds: string[]) => void;
	toggleFolderCollapse:      (folderId: string) => void;
	cleanupSessions:           (scopeKey: string, existingSessionIds: Set<string>) => void;
	getSessionFolderId:        (scopeKey: string, sessionId: string) => string | null;

	/** Internal SSE hooks. */
	_applyFolderCreated: (dto: SessionFolderDto) => void;
	_applyFolderUpdated: (dto: SessionFolderDto) => void;
	_applyFolderDeleted: (folderId: string) => void;
	_setScopeFromServer: (scopeKey: string, dtos: SessionFolderDto[]) => void;
}

// ── Implementation ──────────────────────────────────────────────────────────

const ARCHIVED_SCOPE_PREFIX = '__archived__:';

function makeLocalId(): string {
	return `local_${(typeof crypto !== 'undefined' && crypto.randomUUID) ? crypto.randomUUID() : `${Date.now()}_${Math.random().toString(36).slice(2, 8)}`}`;
}

function dtoToFolder(dto: SessionFolderDto): SessionFolder {
	return {
		id:         dto.id,
		name:       dto.name,
		sessionIds: dto.items.map(i => i.chat_id),
		createdAt:  Date.parse(dto.created_at) || Date.now(),
		parentId:   dto.parent_id ?? null,
	};
}

function setFolderInScope(map: SessionFoldersMap, scope: string, updater: (folders: SessionFolder[]) => SessionFolder[]): SessionFoldersMap {
	const next = updater(map[scope] ?? []);
	return { ...map, [scope]: next };
}

function syncCollapsedAfterFolderCleanup(prevFolders: SessionFolder[], nextFolders: SessionFolder[], collapsed: Set<string>): Set<string> | null {
	const nextIds = new Set(nextFolders.map(f => f.id));
	let nextCollapsed: Set<string> | null = null;
	for (const folder of prevFolders) {
		if (!nextIds.has(folder.id) && collapsed.has(folder.id)) {
			nextCollapsed ??= new Set(collapsed);
			nextCollapsed.delete(folder.id);
		}
	}
	return nextCollapsed;
}

/** Pending local creates: maps `localId` -> serverId resolution promise so subsequent
 *  ops can resolve the canonical id once the POST settles. */
const pendingCreates = new Map<string, Promise<string | null>>();

function isLocalId(id: string): boolean {
	return id.startsWith('local_');
}

async function resolveId(id: string): Promise<string | null> {
	if (!isLocalId(id)) return id;
	const pending = pendingCreates.get(id);
	if (!pending) return null; // unknown local id — already rolled back
	return pending;
}

export const useSessionFoldersStore = create<SessionFoldersStore>((set, get) => ({
	foldersMap:         {},
	collapsedFolderIds: new Set<string>(),
	scopesLoaded:       new Set<string>(),
	loading:            false,
	lastError:          null,

	loadScope: async (scopeKey: string): Promise<void> => {
		if (!scopeKey) return;
		const state = get();
		if (state.scopesLoaded.has(scopeKey)) return;
		// Mark as loading immediately so concurrent calls don't re-fire.
		const nextLoaded = new Set(state.scopesLoaded);
		nextLoaded.add(scopeKey);
		set({ scopesLoaded: nextLoaded, loading: true });

		try {
			const dtos = await LisRpcClient.instance().listSessionFolders(scopeKey) as SessionFolderDto[];
			get()._setScopeFromServer(scopeKey, dtos);
		} catch (err) {
			set({ lastError: errorMessage(err) });
		} finally {
			set({ loading: false });
		}
	},

	getFoldersForScope: (scopeKey: string): SessionFolder[] => {
		if (!scopeKey) return [];
		const state = get();
		// Lazy-load on first read per scope. Fire-and-forget; loading state will
		// trigger a re-render once the data lands.
		if (!state.scopesLoaded.has(scopeKey)) void state.loadScope(scopeKey);
		return state.foldersMap[scopeKey] ?? [];
	},

	createFolder: (scopeKey: string, name: string, parentId?: string | null): SessionFolder => {
		const trimmed = name.trim() || 'New folder';
		const localId = makeLocalId();
		const folder: SessionFolder = {
			id:         localId,
			name:       trimmed,
			sessionIds: [],
			createdAt:  Date.now(),
			parentId:   parentId ?? null,
		};
		const next = setFolderInScope(get().foldersMap, scopeKey, (folders) => [...folders, folder]);
		set({ foldersMap: next });

		// Resolve parentId for the POST: if it's also a local id, we have to wait.
		const postPromise: Promise<string | null> = (async () => {
			let serverParentId: string | null | undefined = parentId ?? null;
			if (parentId && isLocalId(parentId)) {
				serverParentId = await resolveId(parentId);
				if (!serverParentId) {
					rollbackLocalCreate(localId, scopeKey);
					return null;
				}
			}
			try {
				const dto = await LisRpcClient.instance().createSessionFolder(scopeKey, trimmed, serverParentId ?? undefined) as SessionFolderDto;
				// Swap localId for serverId in the foldersMap (and collapsed set).
				get()._applyFolderCreated(dto);
				const stateAfter = get();
				const updated = setFolderInScope(stateAfter.foldersMap, scopeKey, (folders) => folders.filter(f => f.id !== localId));
				set({ foldersMap: updated });
				return dto.id;
			} catch (err) {
				set({ lastError: errorMessage(err) });
				rollbackLocalCreate(localId, scopeKey);
				return null;
			}
		})();
		pendingCreates.set(localId, postPromise);
		void postPromise.finally(() => { pendingCreates.delete(localId); });

		return folder;

		function rollbackLocalCreate(id: string, scope: string): void {
			const stateAfter = get();
			const next = setFolderInScope(stateAfter.foldersMap, scope, (folders) => folders.filter(f => f.id !== id));
			set({ foldersMap: next });
		}
	},

	renameFolder: (scopeKey: string, folderId: string, name: string): void => {
		const trimmed = name.trim();
		if (!trimmed || !scopeKey) return;

		// Optimistic local update.
		const current = get().foldersMap[scopeKey] ?? [];
		const previousName = current.find(f => f.id === folderId)?.name;
		if (previousName === undefined) return;
		if (previousName === trimmed) return;

		const next = setFolderInScope(get().foldersMap, scopeKey, (folders) =>
			folders.map(f => f.id === folderId ? { ...f, name: trimmed } : f),
		);
		set({ foldersMap: next });

		// Resolve to a server id (may be pending).
		void resolveId(folderId).then(async (serverId) => {
			if (!serverId) return; // local create rolled back
			try {
				await LisRpcClient.instance().renameSessionFolder(serverId, trimmed);
			} catch (err) {
				set({ lastError: errorMessage(err) });
				// Rollback local name.
				const rollback = setFolderInScope(get().foldersMap, scopeKey, (folders) =>
					folders.map(f => f.id === folderId ? { ...f, name: previousName } : f),
				);
				set({ foldersMap: rollback });
			}
		});
	},

	deleteFolder: (scopeKey: string, folderId: string): void => {
		if (!scopeKey) return;
		const currentScope = get().foldersMap[scopeKey];
		if (!currentScope) return;

		// Compute ids to delete (folder + descendants), mirroring OC.
		const idsToDelete = new Set<string>([folderId]);
		let changed = true;
		while (changed) {
			changed = false;
			for (const f of currentScope) {
				if (f.parentId && idsToDelete.has(f.parentId) && !idsToDelete.has(f.id)) {
					idsToDelete.add(f.id);
					changed = true;
				}
			}
		}

		// Snapshot deleted folders so we can rollback on server failure.
		const deletedSnapshot = currentScope.filter(f => idsToDelete.has(f.id));

		const next = setFolderInScope(get().foldersMap, scopeKey, (folders) => folders.filter(f => !idsToDelete.has(f.id)));
		// Clean collapsed state.
		let nextCollapsed = get().collapsedFolderIds;
		for (const id of idsToDelete) {
			if (nextCollapsed.has(id)) {
				nextCollapsed = new Set(nextCollapsed);
				nextCollapsed.delete(id);
			}
		}
		set({ foldersMap: next, collapsedFolderIds: nextCollapsed });

		// Only POST the top-level delete — the backend orphans children automatically.
		void resolveId(folderId).then(async (serverId) => {
			if (!serverId) return;
			try {
				await LisRpcClient.instance().deleteSessionFolder(serverId);
			} catch (err) {
				set({ lastError: errorMessage(err) });
				// Rollback the local deletion by re-inserting the snapshot.
				const rollback = setFolderInScope(get().foldersMap, scopeKey, (folders) => [...folders, ...deletedSnapshot]);
				set({ foldersMap: rollback });
			}
		});
	},

	addSessionToFolder: (scopeKey: string, folderId: string, sessionId: string): void => {
		if (!scopeKey || !folderId || !sessionId) return;
		get().addSessionsToFolder(scopeKey, folderId, [sessionId]);
	},

	addSessionsToFolder: (scopeKey: string, folderId: string, sessionIds: string[]): void => {
		if (!scopeKey || !folderId || sessionIds.length === 0) return;
		const scopeFolders = get().foldersMap[scopeKey];
		if (!scopeFolders) return;
		const idSet = new Set(sessionIds.filter(id => typeof id === 'string' && id.length > 0));
		if (idSet.size === 0) return;

		// Snapshot for rollback.
		const snapshot = scopeFolders;

		const nextFolders = scopeFolders.map(folder => {
			const withoutSessions = folder.sessionIds.filter(id => !idSet.has(id));
			if (folder.id === folderId) {
				return { ...folder, sessionIds: [...withoutSessions, ...idSet] };
			}
			if (withoutSessions.length !== folder.sessionIds.length) {
				return { ...folder, sessionIds: withoutSessions };
			}
			return folder;
		});
		const nextMap: SessionFoldersMap = { ...get().foldersMap, [scopeKey]: nextFolders };
		const nextCollapsed = syncCollapsedAfterFolderCleanup(scopeFolders, nextFolders, get().collapsedFolderIds);
		set(nextCollapsed
			? { foldersMap: nextMap, collapsedFolderIds: nextCollapsed }
			: { foldersMap: nextMap });

		void resolveId(folderId).then(async (serverFolderId) => {
			if (!serverFolderId) return;
			try {
				await LisRpcClient.instance().addSessionsToFolder(serverFolderId, [...idSet]);
			} catch (err) {
				set({ lastError: errorMessage(err) });
				set({ foldersMap: { ...get().foldersMap, [scopeKey]: snapshot } });
			}
		});
	},

	removeSessionFromFolder: (scopeKey: string, sessionId: string): void => {
		if (!scopeKey || !sessionId) return;
		const scopeFolders = get().foldersMap[scopeKey];
		if (!scopeFolders) return;

		// Find which folder owns this session — we only POST to that one.
		const owningFolder = scopeFolders.find(f => f.sessionIds.includes(sessionId));
		if (!owningFolder) return;

		const snapshot = scopeFolders;
		const nextFolders = scopeFolders.map(folder => {
			if (folder.id !== owningFolder.id) return folder;
			return { ...folder, sessionIds: folder.sessionIds.filter(id => id !== sessionId) };
		});
		const nextMap: SessionFoldersMap = { ...get().foldersMap, [scopeKey]: nextFolders };
		const nextCollapsed = syncCollapsedAfterFolderCleanup(scopeFolders, nextFolders, get().collapsedFolderIds);
		set(nextCollapsed
			? { foldersMap: nextMap, collapsedFolderIds: nextCollapsed }
			: { foldersMap: nextMap });

		void resolveId(owningFolder.id).then(async (serverFolderId) => {
			if (!serverFolderId) return;
			try {
				await LisRpcClient.instance().removeSessionFromFolder(serverFolderId, sessionId);
			} catch (err) {
				set({ lastError: errorMessage(err) });
				set({ foldersMap: { ...get().foldersMap, [scopeKey]: snapshot } });
			}
		});
	},

	removeSessionsFromFolders: (scopeKey: string, sessionIds: string[]): void => {
		if (!scopeKey || sessionIds.length === 0) return;
		const idSet = new Set(sessionIds.filter(id => typeof id === 'string' && id.length > 0));
		if (idSet.size === 0) return;
		for (const sid of idSet) {
			get().removeSessionFromFolder(scopeKey, sid);
		}
	},

	toggleFolderCollapse: (folderId: string): void => {
		const collapsed = get().collapsedFolderIds;
		const next = new Set(collapsed);
		if (next.has(folderId)) next.delete(folderId);
		else                    next.add(folderId);
		set({ collapsedFolderIds: next });
	},

	cleanupSessions: (scopeKey: string, existingSessionIds: Set<string>): void => {
		if (!scopeKey) return;
		const scopeFolders = get().foldersMap[scopeKey];
		if (!scopeFolders || scopeFolders.length === 0) return;

		let changed = false;
		const filtered = scopeFolders.map(folder => {
			const next = folder.sessionIds.filter(id => existingSessionIds.has(id));
			if (next.length !== folder.sessionIds.length) {
				changed = true;
				return { ...folder, sessionIds: next };
			}
			return folder;
		});

		// Prune empty archived folders (matches OC).
		const pruneEmpty = scopeKey.startsWith(ARCHIVED_SCOPE_PREFIX);
		const nextFolders = pruneEmpty ? filtered.filter(f => f.sessionIds.length > 0) : filtered;
		if (nextFolders.length !== filtered.length) changed = true;

		if (!changed) return;
		const nextMap: SessionFoldersMap = { ...get().foldersMap, [scopeKey]: nextFolders };
		const nextCollapsed = syncCollapsedAfterFolderCleanup(scopeFolders, nextFolders, get().collapsedFolderIds);
		set(nextCollapsed
			? { foldersMap: nextMap, collapsedFolderIds: nextCollapsed }
			: { foldersMap: nextMap });
		// Pruning here is local-only — the backend's CleanupAsync runs separately. We do not
		// POST removal for every stale session id here; SSE folder_updated arrives if the
		// backend cleanup runs and re-syncs us.
	},

	getSessionFolderId: (scopeKey: string, sessionId: string): string | null => {
		if (!scopeKey || !sessionId) return null;
		const scopeFolders = get().foldersMap[scopeKey];
		if (!scopeFolders) return null;
		for (const folder of scopeFolders) {
			if (folder.sessionIds.includes(sessionId)) return folder.id;
		}
		return null;
	},

	// ── SSE handlers ───────────────────────────────────────────────

	_applyFolderCreated: (dto: SessionFolderDto): void => {
		const scope = dto.scope_key;
		const existing = get().foldersMap[scope] ?? [];
		if (existing.some(f => f.id === dto.id)) return; // idempotent

		const next = setFolderInScope(get().foldersMap, scope, (folders) => [...folders, dtoToFolder(dto)]);
		set({ foldersMap: next });
	},

	_applyFolderUpdated: (dto: SessionFolderDto): void => {
		const scope = dto.scope_key;
		const existing = get().foldersMap[scope] ?? [];
		const incoming = dtoToFolder(dto);
		const found = existing.some(f => f.id === dto.id);
		const next = setFolderInScope(get().foldersMap, scope, (folders) =>
			found ? folders.map(f => f.id === dto.id ? incoming : f) : [...folders, incoming],
		);
		set({ foldersMap: next });
	},

	_applyFolderDeleted: (folderId: string): void => {
		const map = get().foldersMap;
		let mutated = false;
		const nextMap: SessionFoldersMap = {};
		for (const [scope, folders] of Object.entries(map)) {
			const filtered = folders.filter(f => f.id !== folderId);
			if (filtered.length !== folders.length) mutated = true;
			nextMap[scope] = filtered;
		}
		if (!mutated) return;
		let collapsed = get().collapsedFolderIds;
		if (collapsed.has(folderId)) {
			collapsed = new Set(collapsed);
			collapsed.delete(folderId);
		}
		set({ foldersMap: nextMap, collapsedFolderIds: collapsed });
	},

	_setScopeFromServer: (scopeKey: string, dtos: SessionFolderDto[]): void => {
		const folders = dtos.map(dtoToFolder);
		const next: SessionFoldersMap = { ...get().foldersMap, [scopeKey]: folders };
		set({ foldersMap: next });
	},
}));

/** Subscribe to folder SSE events. Call once after `LisRpcClient.install`. */
export function installSessionFoldersSync(): () => void {
	const client = LisRpcClient.instance();
	const store = useSessionFoldersStore.getState();

	return client.onSse((ev) => {
		switch (ev.event) {
			case 'folder_created': {
				const dto = extractFolder(ev.data);
				if (dto) store._applyFolderCreated(dto);
				return;
			}
			case 'folder_updated': {
				const dto = extractFolder(ev.data);
				if (dto) store._applyFolderUpdated(dto);
				return;
			}
			case 'folder_deleted': {
				const data = ev.data as { folder_id?: string } | null;
				if (data?.folder_id) store._applyFolderDeleted(data.folder_id);
				return;
			}
			default:
				return;
		}
	});
}

function extractFolder(data: unknown): SessionFolderDto | null {
	if (!data || typeof data !== 'object') return null;
	const wrapped = data as { folder?: SessionFolderDto };
	if (wrapped.folder && typeof wrapped.folder === 'object') return wrapped.folder;
	return data as SessionFolderDto;
}

function errorMessage(err: unknown): string {
	if (err instanceof LisRpcError) return err.message;
	if (err instanceof Error)        return err.message;
	return String(err);
}
