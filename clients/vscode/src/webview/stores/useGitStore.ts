import { useRef } from 'react';
import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native git store. Backed by the Stage-5 backend:
 *
 *   - `GET /api/git/status?path=`   → branch + ahead/behind + dirty.
 *   - `GET /api/git/branches?path=` → local + remote branch lists.
 *
 * Mirrors OC's `useGitStore` surface (per-directory state map, lazy fetchers,
 * selector hooks) so byte-identical OC consumers like SessionSidebar,
 * RightSidebarTabs, GitView, and SidebarFilesTree compile and behave.
 *
 * Out of scope today: commit log, identity (managed via `useGitIdentitiesStore`,
 * itself a no-op until Stage X), diff prefetch, file-list status. The backend
 * doesn't expose those endpoints yet; the corresponding methods are typed
 * no-ops so consumers that touch them degrade cleanly.
 */

// ── Wire shapes (Lis backend) ───────────────────────────────────────────────

interface RepoStatusDto {
	is_git_repo:    boolean;
	branch?:        string | null;
	ahead?:         number;
	behind?:        number;
	dirty?:         boolean;
	last_commit?:   { sha: string; message: string } | null;
}

interface BranchesDto {
	local:   string[];
	remote:  string[];
	current: string | null;
}

// ── Public surface (OC-compatible enough) ───────────────────────────────────

export interface GitStatusSummary {
	current?:   string | null;
	ahead?:     number;
	behind?:    number;
	dirty?:     boolean;
	files?:     unknown[];   // OC has detailed file list; Lis doesn't expose yet
}

export interface GitDirectoryState {
	isGitRepo:           boolean | null;
	status:              GitStatusSummary | null;
	branches:            string[] | null;
	allBranches:         { local: string[]; remote: string[] } | null;
	log:                 unknown[] | null;
	identity:            unknown | null;
	isLoadingStatus:     boolean;
	isLoadingBranches:   boolean;
	isLoadingLog:        boolean;
	isLoadingIdentity:   boolean;
	lastStatusAt:        number;
}

interface GitStore {
	directories:  Map<string, GitDirectoryState>;

	fetchStatus:    (directory: string, opts?: { force?: boolean }) => Promise<void>;
	fetchBranches:  (directory: string) => Promise<void>;
	ensureStatus:   (directory: string) => Promise<void>;
	ensureAll:      (directory: string) => Promise<void>;
	fetchAll:       (directory: string) => Promise<void>;

	// No-op surface for OC parity (backend doesn't expose these yet):
	fetchLog:                       (directory: string) => Promise<void>;
	fetchIdentity:                  (directory: string) => Promise<void>;
	prefetchDiffs:                  () => void;
	setLogMaxCount:                 (n: number) => void;
	moveStatusPathsOptimistically:  () => void;
	restoreStatus:                  () => void;
	bumpIndexRevision:              () => void;
	setDiff:                        () => void;
	setActiveDirectory:             (directory: string | null) => void;
}

// ── Helpers ─────────────────────────────────────────────────────────────────

const STATUS_TTL_MS = 30_000;

function emptyState(): GitDirectoryState {
	return {
		isGitRepo:         null,
		status:            null,
		branches:          null,
		allBranches:       null,
		log:               null,
		identity:          null,
		isLoadingStatus:   false,
		isLoadingBranches: false,
		isLoadingLog:      false,
		isLoadingIdentity: false,
		lastStatusAt:      0,
	};
}

function patchDirectory(map: Map<string, GitDirectoryState>, dir: string, patch: Partial<GitDirectoryState>): Map<string, GitDirectoryState> {
	const next = new Map(map);
	const cur  = next.get(dir) ?? emptyState();
	next.set(dir, { ...cur, ...patch });
	return next;
}

// ── Store ───────────────────────────────────────────────────────────────────

export const useGitStore = create<GitStore>((set, get) => ({
	directories: new Map(),

	fetchStatus: async (directory, opts) => {
		if (!directory) return;
		const cur = get().directories.get(directory);
		if (!opts?.force && cur?.lastStatusAt && Date.now() - cur.lastStatusAt < STATUS_TTL_MS) return;

		set(s => ({ directories: patchDirectory(s.directories, directory, { isLoadingStatus: true }) }));
		try {
			const dto = await LisRpcClient.instance().getRepoStatus(directory) as RepoStatusDto;
			set(s => ({
				directories: patchDirectory(s.directories, directory, {
					isGitRepo:       dto.is_git_repo,
					status:          dto.is_git_repo ? {
						current: dto.branch ?? null,
						ahead:   dto.ahead,
						behind:  dto.behind,
						dirty:   dto.dirty,
						files:   [],
					} : null,
					isLoadingStatus: false,
					lastStatusAt:    Date.now(),
				}),
			}));
		} catch (err) {
			// Mark as non-repo on failure so the UI doesn't keep retrying every render.
			const isNotRepo = err instanceof LisRpcError && (err.status === 404 || err.status === 400);
			set(s => ({
				directories: patchDirectory(s.directories, directory, {
					isGitRepo:       isNotRepo ? false : (cur?.isGitRepo ?? false),
					isLoadingStatus: false,
					lastStatusAt:    Date.now(),
				}),
			}));
		}
	},

	fetchBranches: async (directory) => {
		if (!directory) return;
		set(s => ({ directories: patchDirectory(s.directories, directory, { isLoadingBranches: true }) }));
		try {
			const dto = await LisRpcClient.instance().listBranches(directory) as BranchesDto;
			set(s => ({
				directories: patchDirectory(s.directories, directory, {
					branches:          [...dto.local, ...dto.remote],
					allBranches:       { local: dto.local, remote: dto.remote },
					isLoadingBranches: false,
				}),
			}));
		} catch {
			set(s => ({ directories: patchDirectory(s.directories, directory, { isLoadingBranches: false }) }));
		}
	},

	ensureStatus: async (directory) => {
		if (!directory) return;
		const cur = get().directories.get(directory);
		if (cur && cur.lastStatusAt && Date.now() - cur.lastStatusAt < STATUS_TTL_MS) return;
		if (cur?.isLoadingStatus) return;
		await get().fetchStatus(directory);
	},

	ensureAll: async (directory) => {
		await Promise.all([get().fetchStatus(directory), get().fetchBranches(directory)]);
	},

	fetchAll: async (directory) => {
		await Promise.all([get().fetchStatus(directory, { force: true }), get().fetchBranches(directory)]);
	},

	// ── No-op parity surface ──────────────────────────────
	fetchLog:                      async () => { /* backend exposes no /api/git/log yet */ },
	fetchIdentity:                 async () => { /* delegated to useGitIdentitiesStore */ },
	prefetchDiffs:                 () => { /* noop */ },
	setLogMaxCount:                () => { /* noop */ },
	moveStatusPathsOptimistically: () => { /* noop */ },
	restoreStatus:                 () => { /* noop */ },
	bumpIndexRevision:             () => { /* noop */ },
	setDiff:                       () => { /* noop */ },
	setActiveDirectory:            () => { /* tracked by useDirectoryStore */ },
}));

// ── Selector hooks ──────────────────────────────────────────────────────────

const EMPTY_ARR: never[] = [];

export function useGitStatus(directory: string | null): GitStatusSummary | null {
	return useGitStore(state => directory ? state.directories.get(directory)?.status ?? null : null);
}

export function useGitBranches(directory: string | null): string[] {
	return useGitStore(state => directory ? state.directories.get(directory)?.branches ?? EMPTY_ARR : EMPTY_ARR);
}

export function useGitLog(directory: string | null): unknown[] {
	return useGitStore(state => directory ? state.directories.get(directory)?.log ?? EMPTY_ARR : EMPTY_ARR);
}

export function useGitIdentity(directory: string | null): unknown {
	return useGitStore(state => directory ? state.directories.get(directory)?.identity ?? null : null);
}

export function useIsGitRepo(directory: string | null): boolean | null {
	return useGitStore(state => directory ? state.directories.get(directory)?.isGitRepo ?? null : null);
}

export function useGitFileCount(directory: string | null): number {
	return useGitStore(state => directory ? (state.directories.get(directory)?.status?.files?.length ?? 0) : 0);
}

export function useGitBranchLabel(directory: string | null): string | null {
	return useGitStore(state => directory ? (state.directories.get(directory)?.status?.current?.trim() ?? null) : null);
}

export function useGitAllBranches(directory: string | null): { local: string[]; remote: string[] } | null {
	return useGitStore(state => directory ? state.directories.get(directory)?.allBranches ?? null : null);
}

export function useGitLoadingStatus(directory: string | null): boolean {
	return useGitStore(state => directory ? state.directories.get(directory)?.isLoadingStatus ?? false : false);
}

export function useGitLoadingBranches(directory: string | null): boolean {
	return useGitStore(state => directory ? state.directories.get(directory)?.isLoadingBranches ?? false : false);
}

export function useGitLoadingLog(directory: string | null): boolean {
	return useGitStore(state => directory ? state.directories.get(directory)?.isLoadingLog ?? false : false);
}

export interface GitRepoStatusSummary {
	isGitRepo: boolean | null;
	branch:    string | null;
}

/**
 * Reactive map of `{isGitRepo, branch}` for the given paths. Memoises the
 * returned Map by identity when none of the underlying entries changed, so
 * downstream useMemo/useEffect chains stay stable across re-renders.
 */
export function useGitRepoStatusMap(directories: string[]): Map<string, GitRepoStatusSummary> {
	const cacheRef = useRef<Map<string, GitRepoStatusSummary>>(new Map());
	return useGitStore((state) => {
		const prev = cacheRef.current;
		let same = prev.size === directories.length;
		if (same) {
			for (const dir of directories) {
				const d  = state.directories.get(dir);
				const pv = prev.get(dir);
				if (!pv
					|| (d?.isGitRepo ?? null) !== pv.isGitRepo
					|| (d?.status?.current ?? null) !== pv.branch) { same = false; break; }
			}
		}
		if (same) return prev;
		const result = new Map<string, GitRepoStatusSummary>();
		for (const dir of directories) {
			const d = state.directories.get(dir);
			result.set(dir, { isGitRepo: d?.isGitRepo ?? null, branch: d?.status?.current ?? null });
		}
		cacheRef.current = result;
		return result;
	});
}
