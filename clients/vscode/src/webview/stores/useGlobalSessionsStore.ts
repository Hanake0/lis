import { create } from 'zustand';

import type { Session } from '@/lib/opencode-sdk-stub';

/**
 * Aggregate view of every chat Lis knows about, regardless of project.
 *
 * The store works in OC-shaped `Session` objects so the byte-identical
 * `components/session/`, `CommandPalette`, `Header` and `SessionSwitcher`
 * surfaces (ported from OC) keep compiling. Lis converts its native
 * `ChatSummary` to this shape at the seam (`sidebarOC.tsx#toOCSession`).
 *
 * Data flow:
 *  - Bootstrap (and live updates) populate the store via `applySnapshot` /
 *    `upsertSession` from the chat sync layer (today: WS frames from the
 *    extension host; after Stage 13: REST + SSE).
 *  - `loadSessions` is a placeholder that returns the current snapshot
 *    (merging an optional fallback). It will be wired to `GET /api/chats`
 *    when Stage 13 lands; callers can already invoke it without breaking.
 *
 * The list manipulation logic (signature-based dedup, archive bucket move,
 * scope-by-directory map) is ported verbatim from OC's
 * `useGlobalSessionsStore.ts` — pure data, no network.
 */

type GlobalSessionsStatus = 'idle' | 'loading' | 'ready' | 'error';

interface LoadResult {
	activeSessions:   Session[];
	archivedSessions: Session[];
}

interface GlobalSessionsState {
	activeSessions:      Session[];
	archivedSessions:    Session[];
	sessionsByDirectory: Map<string, Session[]>;
	hasLoaded:           boolean;
	status:              GlobalSessionsStatus;
	loadSessions:        (fallbackActive?: Session[]) => Promise<LoadResult>;
	applySnapshot:       (active: Session[], archived: Session[], status?: GlobalSessionsStatus) => void;
	upsertSession:       (session: Session) => void;
	removeSessions:      (ids: Iterable<string>) => void;
	archiveSessions:     (ids: Iterable<string>, archivedAt?: number) => void;
}

// ── Pure helpers ────────────────────────────────────────────────────────────

function normalizePath(value?: string | null): string | null {
	if (typeof value !== 'string') return null;
	const trimmed = value.trim();
	if (!trimmed) return null;
	const replaced = trimmed.replace(/\\/g, '/');
	if (replaced === '/') return '/';
	return replaced.length > 1 ? replaced.replace(/\/+$/, '') : replaced;
}

export function resolveGlobalSessionDirectory(session: Session): string | null {
	const record = session as Session & {
		directory?: string | null;
		project?:   { worktree?: string | null } | null;
	};
	return normalizePath(record.directory ?? null)
		?? normalizePath(record.project?.worktree ?? null);
}

function buildSessionsByDirectory(sessions: Session[]): Map<string, Session[]> {
	const next = new Map<string, Session[]>();
	for (const session of sessions) {
		const dir = resolveGlobalSessionDirectory(session);
		if (!dir) continue;
		const existing = next.get(dir);
		if (existing) { existing.push(session); continue; }
		next.set(dir, [session]);
	}
	return next;
}

function sessionSignature(session: Session): string {
	return [
		session.id,
		session.title ?? '',
		session.time?.created ?? 0,
		session.time?.updated ?? 0,
		session.time?.archived ?? 0,
		session.share?.url ?? '',
		resolveGlobalSessionDirectory(session) ?? '',
	].join(':');
}

function sameSessionList(prev: Session[], next: Session[]): boolean {
	if (prev === next) return true;
	if (prev.length !== next.length) return false;
	for (let i = 0; i < prev.length; i++) {
		if (sessionSignature(prev[i]) !== sessionSignature(next[i])) return false;
	}
	return true;
}

function upsertSessionIntoList(list: Session[], session: Session): Session[] {
	const idx = list.findIndex(s => s.id === session.id);
	if (idx === -1) return [session, ...list];
	if (sessionSignature(list[idx]) === sessionSignature(session)) return list;
	const next = [...list];
	next[idx] = session;
	return next;
}

function mergeSessionLists(existing: Session[], incoming?: Session[]): Session[] {
	if (!incoming || incoming.length === 0) return existing;
	if (existing.length === 0)               return incoming;

	const byId = new Map(existing.map(s => [s.id, s] as const));
	incoming.forEach(s => byId.set(s.id, s));

	const ordered: Session[] = [];
	const seen = new Set<string>();
	existing.forEach(s => {
		const next = byId.get(s.id);
		if (!next) return;
		ordered.push(next);
		seen.add(s.id);
	});
	incoming.forEach(s => {
		if (seen.has(s.id)) return;
		const next = byId.get(s.id);
		if (next) { ordered.push(next); seen.add(s.id); }
	});
	return ordered;
}

function applySnapshotInternal(
	state:    GlobalSessionsState,
	active:   Session[],
	archived: Session[],
	status:   GlobalSessionsStatus,
): Partial<GlobalSessionsState> | GlobalSessionsState {
	const nextActive   = sameSessionList(state.activeSessions,   active)   ? state.activeSessions   : active;
	const nextArchived = sameSessionList(state.archivedSessions, archived) ? state.archivedSessions : archived;
	const nextByDir    = nextActive === state.activeSessions ? state.sessionsByDirectory : buildSessionsByDirectory(nextActive);

	if (
		nextActive    === state.activeSessions
		&& nextArchived === state.archivedSessions
		&& nextByDir    === state.sessionsByDirectory
		&& state.hasLoaded
		&& state.status === status
	) return state;

	return {
		activeSessions:      nextActive,
		archivedSessions:    nextArchived,
		sessionsByDirectory: nextByDir,
		hasLoaded:           true,
		status,
	};
}

// ── Store ───────────────────────────────────────────────────────────────────

let inflightLoad: Promise<LoadResult> | null = null;

export const useGlobalSessionsStore = create<GlobalSessionsState>((set, get) => ({
	activeSessions:      [],
	archivedSessions:    [],
	sessionsByDirectory: new Map(),
	hasLoaded:           false,
	status:              'idle',

	applySnapshot: (active, archived, status = 'ready') => {
		set(state => applySnapshotInternal(state, active, archived, status));
	},

	loadSessions: async (fallbackActive) => {
		if (inflightLoad) return inflightLoad;

		set(state => state.status === 'loading' ? state : { status: 'loading' });

		// Stage 13 will wire this to LisRpcClient.listChats(). Until then the
		// sidebar bootstrap pushes snapshots via applySnapshot, and this just
		// merges any caller-supplied fallback into the current snapshot.
		//
		// NOTE on the cleanup pattern: we clear `inflightLoad` in the OUTER async
		// function's finally rather than the IIFE's. The body of this load is
		// synchronous (no awaits), so an IIFE finally would null inflightLoad
		// during the IIFE invocation itself — leaving `inflightLoad` reassigned
		// to a stale resolved Promise after this function returns. Clearing via
		// the outer `try/finally` around `await` correctly defers cleanup until
		// after the result is observed.
		const myLoad = (async (): Promise<LoadResult> => {
			const current = get();
			const merged  = mergeSessionLists(current.activeSessions, fallbackActive);
			set(state => applySnapshotInternal(state, merged, current.archivedSessions, 'ready'));
			return { activeSessions: merged, archivedSessions: current.archivedSessions };
		})();
		inflightLoad = myLoad;
		try     { return await myLoad; }
		finally { if (inflightLoad === myLoad) inflightLoad = null; }
	},

	upsertSession: (session) => {
		set(state => {
			const isArchived = Boolean(session.time?.archived);
			const nextActive   = isArchived
				? state.activeSessions.filter(s => s.id !== session.id)
				: upsertSessionIntoList(state.activeSessions, session);
			const nextArchived = isArchived
				? upsertSessionIntoList(state.archivedSessions, session)
				: state.archivedSessions.filter(s => s.id !== session.id);

			if (nextActive === state.activeSessions && nextArchived === state.archivedSessions) return state;

			return {
				activeSessions:      nextActive,
				archivedSessions:    nextArchived,
				sessionsByDirectory: nextActive === state.activeSessions ? state.sessionsByDirectory : buildSessionsByDirectory(nextActive),
			};
		});
	},

	removeSessions: (ids) => {
		const idSet = ids instanceof Set ? ids : new Set(ids);
		if (idSet.size === 0) return;

		set(state => {
			const nextActive   = state.activeSessions.filter(s => !idSet.has(s.id));
			const nextArchived = state.archivedSessions.filter(s => !idSet.has(s.id));
			if (nextActive.length === state.activeSessions.length && nextArchived.length === state.archivedSessions.length) return state;
			return {
				activeSessions:      nextActive,
				archivedSessions:    nextArchived,
				sessionsByDirectory: buildSessionsByDirectory(nextActive),
			};
		});
	},

	archiveSessions: (ids, archivedAt = Date.now()) => {
		const idSet = ids instanceof Set ? ids : new Set(ids);
		if (idSet.size === 0) return;

		set(state => {
			const moved: Session[] = [];
			const nextActive = state.activeSessions.filter(s => {
				if (!idSet.has(s.id)) return true;
				moved.push({ ...s, time: { ...s.time, archived: archivedAt } });
				return false;
			});
			if (moved.length === 0) return state;
			const remainingArchived = state.archivedSessions.filter(s => !idSet.has(s.id));
			return {
				activeSessions:      nextActive,
				archivedSessions:    [...moved, ...remainingArchived],
				sessionsByDirectory: buildSessionsByDirectory(nextActive),
			};
		});
	},
}));

export async function ensureGlobalSessionsLoaded(fallbackActive?: Session[]): Promise<LoadResult> {
	const state = useGlobalSessionsStore.getState();
	if (state.hasLoaded && state.status !== 'error') {
		return { activeSessions: state.activeSessions, archivedSessions: state.archivedSessions };
	}
	return state.loadSessions(fallbackActive);
}

export async function refreshGlobalSessions(fallbackActive?: Session[]): Promise<LoadResult> {
	return useGlobalSessionsStore.getState().loadSessions(fallbackActive);
}
