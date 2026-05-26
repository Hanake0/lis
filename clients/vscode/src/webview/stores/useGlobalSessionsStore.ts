import { create } from 'zustand';

import type { Session } from '@/lib/opencode-sdk-stub';
import { LisRpcClient, type LisChatSummary } from '@/lib/lis/rpc-client';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { normalizePath } from '@/components/session/sidebar/utils';

/**
 * Aggregate view of every chat Lis knows about, regardless of project.
 *
 * The store works in OC-shaped `Session` objects so the byte-identical
 * `components/session/`, `CommandPalette`, `Header` and `SessionSwitcher`
 * surfaces (ported from OC) keep compiling. Lis converts its native
 * `ChatSummary` to this shape via `chatSummaryToSession` (below).
 *
 * Data flow:
 *  - `loadSessions` pulls the canonical snapshot from `GET /api/chats` and
 *    seeds the store. Optional `fallbackActive` from the caller (the sync
 *    layer's local buffer) is merged on top — useful for the brief window
 *    where a newly-created chat exists locally before the SSE event lands.
 *  - Live updates arrive through SSE on `/api/events` and call
 *    `upsertSession` / `removeSessions` / `archiveSessions` directly.
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

export function resolveGlobalSessionDirectory(session: Session): string | null {
	const record = session as Session & {
		directory?:  string | null;
		project?:    { worktree?: string | null } | null;
		projectId?:  string | null;
	};
	const direct = normalizePath(record.directory ?? null)
		?? normalizePath(record.project?.worktree ?? null);
	if (direct) return direct;
	// Lis chats track their project via `project_id` (external_id), not a directory
	// string. Resolve it against the live projects store so sessions group under
	// their project bucket in the OC sidebar.
	if (record.projectId) {
		const projects = useProjectsStore.getState().projects;
		const match = projects.find((p) => p.id === record.projectId);
		if (match?.path) return normalizePath(match.path);
	}
	return null;
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

/**
 * Map the Lis chat-summary wire shape into the OC `Session` the sidebar expects.
 * Time fields stay numeric (epoch ms) so the same byte-identical group/sort code
 * in OC's `SessionSidebar` works unchanged.
 */
export function chatSummaryToSession(row: LisChatSummary): Session {
	const createdMs  = row.created_at  ? Date.parse(row.created_at)  : undefined;
	const updatedMs  = row.updated_at  ? Date.parse(row.updated_at)  : undefined;
	const archivedMs = row.archived_at ? Date.parse(row.archived_at) : undefined;
	// Resolve project_id → project.path so the OC sidebar's directory-based
	// grouping (useProjectSessionLists) buckets the chat under its project.
	// If the project hasn't loaded yet we leave it null and re-map via the
	// projects-store subscription below once they do. Chats with no project
	// binding fall back to the active workspace directory so they're at
	// least visible somewhere — otherwise the user has 41 invisible chats.
	const { projects } = useProjectsStore.getState();
	const projectPath = row.project_id
		? normalizePath(projects.find((p) => p.id === row.project_id)?.path ?? null)
		: null;
	// Chat-scoped directory override (15c.6) takes precedence over the project
	// root for "what cwd does this chat's composer worktree pill display".
	// `directory` on the Session remains the GROUPING key (project root) so
	// the sidebar buckets chats under their project; `chatDirectory` carries
	// the per-chat override for the composer pill and tool cwd.
	const chatDirectory = normalizePath(row.directory ?? null);
	return {
		id:            row.chat_id,
		title:         row.name ?? undefined,
		directory:     projectPath,
		chatDirectory: chatDirectory,
		projectId:     row.project_id,
		createdAt:     createdMs,
		updatedAt:     updatedMs,
		time:          { created: createdMs, updated: updatedMs, archived: archivedMs },
	} as Session;
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

		// Pull from `GET /api/chats`, bucket by `archived_at`, and merge any
		// caller-supplied fallback into the active bucket on top — `mergeSessionLists`
		// preserves server order while letting the fallback override matched rows and
		// append fallback-only rows (the local sync layer's newer-than-the-server view).
		//
		// On RPC failure (server unreachable, client not yet installed) we keep the
		// existing in-memory snapshot intact and merge the fallback into THAT, so the
		// sidebar degrades gracefully rather than blanking.
		const myLoad = (async (): Promise<LoadResult> => {
			try {
				const rows = await LisRpcClient.instance().listChats();
				const mapped = rows.map(chatSummaryToSession);
				const serverActive   = mapped.filter(s => !s.time?.archived);
				const serverArchived = mapped.filter(s =>  s.time?.archived);
				const mergedActive   = mergeSessionLists(serverActive, fallbackActive);
				set(state => applySnapshotInternal(state, mergedActive, serverArchived, 'ready'));
				return { activeSessions: mergedActive, archivedSessions: serverArchived };
			} catch {
				const current = get();
				const merged  = mergeSessionLists(current.activeSessions, fallbackActive);
				set(state => applySnapshotInternal(state, merged, current.archivedSessions, 'error'));
				return { activeSessions: merged, archivedSessions: current.archivedSessions };
			}
		})();
		inflightLoad = myLoad;
		// Outer try/finally so the latch outlives the await and clears only after the
		// promise is observed (avoids the IIFE-finally race where the latch is reset
		// to a stale resolved Promise mid-call).
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

// Debug surface for the e2e harness: exposes the store on `window` so a
// Playwright spec can force a `loadSessions()` round-trip if the SSE event
// missed the freshly-seeded chat (race between REST-seed and SSE-channel
// setup). Production webviews never read this — it's behind the same global
// surface other stores use.
if (typeof window !== 'undefined') {
	(window as unknown as { __lis_globalSessions?: unknown }).__lis_globalSessions = useGlobalSessionsStore;
}

// Projects load asynchronously, often after the first `loadSessions` call. When
// the projects array transitions (e.g. empty → populated, or a project path
// changes), re-resolve `directory` on every cached session so the OC sidebar
// re-buckets them under the right project. We keep the original Session list
// and only patch the directory; this is cheap and avoids a network round-trip.
let lastProjectsSig: string | null = null;
useProjectsStore.subscribe((state) => {
	const sig = state.projects.map((p) => `${p.id}@${p.path}`).join('|');
	if (sig === lastProjectsSig) return;
	lastProjectsSig = sig;
	const byId = new Map(state.projects.map((p) => [p.id, normalizePath(p.path)] as const));
	const remap = (list: Session[]): Session[] => {
		let mutated = false;
		const next = list.map((s) => {
			const projectId = (s as Session & { projectId?: string | null }).projectId ?? null;
			if (!projectId) return s;
			const newDir = byId.get(projectId) ?? null;
			const curDir = (s as Session & { directory?: string | null }).directory ?? null;
			if (newDir === curDir) return s;
			mutated = true;
			return { ...s, directory: newDir } as Session;
		});
		return mutated ? next : list;
	};
	const store = useGlobalSessionsStore.getState();
	const nextActive   = remap(store.activeSessions);
	const nextArchived = remap(store.archivedSessions);
	if (nextActive === store.activeSessions && nextArchived === store.archivedSessions) return;
	useGlobalSessionsStore.setState({
		activeSessions:      nextActive,
		archivedSessions:    nextArchived,
		sessionsByDirectory: buildSessionsByDirectory(nextActive),
	});
});

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
