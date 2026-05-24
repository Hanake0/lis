import { create } from 'zustand';

import type { GitHubPullRequestStatus, RuntimeAPIs } from '@/lib/api/types';
import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Per-entry pull-request tracker for the session sidebar and git view.
 *
 * Backed by the enriched `POST /api/github/pr-status/by-directory` endpoint
 * (Stage 8 backend, expanded for parity with OC's `GitHubPullRequestStatus`).
 *
 * Surface mirrors OC's desktop `useGitHubPrStatusStore` so byte-identical
 * consumers (SessionSidebar, PullRequestSection, GitView, sidebar/utils)
 * keep compiling. The polling/watcher/discovery machinery is ported from
 * OC verbatim with one substitution: where OC's desktop runtime called its
 * own `RuntimeAPIs.github.prStatus(...)` bridge, Lis fetches via
 * `LisRpcClient.getPrStatusByDirectory(...)` and unpacks the single-target
 * result map.
 *
 * Per-entry lifecycle:
 *   1. `ensureEntry(key)` — caller registers interest (idempotent).
 *   2. `setParams(key, params)` — provides directory+branch+auth context.
 *   3. `startWatching(key)` — increments watcher refcount; the first watcher
 *      triggers an immediate refresh and starts a poll loop. Subsequent
 *      watchers just add to the count.
 *   4. The poll loop adapts cadence: discovery for branches without a PR,
 *      busy (60 s) for PRs with pending checks, default (2 min) for PRs
 *      with resolved checks, stable (5 min) when nothing's changing.
 *   5. `stopWatching(key)` — decrements refcount; the last unwatch tears
 *      down timers.
 *
 * Multi-entry coalescing: when two keys point at the same physical
 * `directory::branch` signature (e.g. via different chat aliases), a single
 * HTTP fetch updates every entry with that signature.
 */

// ── Cadence constants (ported verbatim from OC) ─────────────────────────────

const PR_REVALIDATE_TTL_MS         = 90_000;
const PR_REVALIDATE_INTERVAL_MS    = 15_000;
const PR_DISCOVERY_INTERVAL_MS     = 5 * 60_000;
const PR_BOOTSTRAP_RETRY_DELAYS_MS = [2_000, 5_000] as const;
const PR_OPEN_BUSY_INTERVAL_MS     = 60_000;
const PR_OPEN_DEFAULT_INTERVAL_MS  = 2 * 60_000;
const PR_OPEN_STABLE_INTERVAL_MS   = 5 * 60_000;

// ── Public types ────────────────────────────────────────────────────────────

export interface PrTrackingTarget {
	directory:   string;
	branch:      string;
	remoteName?: string | null;
}

export interface PrRuntimeParams {
	directory:         string;
	branch:            string;
	remoteName:        string | null;
	canShow:           boolean;
	github?:           RuntimeAPIs['github'];
	githubAuthChecked: boolean;
	githubConnected:   boolean | null;
}

interface PrEntryIdentity {
	directory:  string;
	branch:     string;
	remoteName: string | null;
}

export interface PrStatusEntry {
	status:                  GitHubPullRequestStatus | null;
	isLoading:               boolean;
	error:                   string | null;
	isInitialStatusResolved: boolean;
	lastRefreshAt:           number;
	lastDiscoveryPollAt:     number;
	watchers:                number;
	params:                  PrRuntimeParams | null;
	identity:                PrEntryIdentity | null;
	resolvedRemoteName:      string | null;
}

interface RefreshOptions {
	force?:               boolean;
	onlyExistingPr?:      boolean;
	silent?:              boolean;
	markInitialResolved?: boolean;
}

interface GitHubPrStatusStore {
	entries:             Record<string, PrStatusEntry>;
	activeRequestCount:  number;
	totalRequestCount:   number;

	ensureEntry:    (key: string) => void;
	setParams:      (key: string, params: PrRuntimeParams) => void;
	startWatching:  (key: string) => void;
	stopWatching:   (key: string) => void;
	refresh:        (key: string, options?: RefreshOptions) => Promise<void>;
	refreshTargets: (targets: PrTrackingTarget[], options?: RefreshOptions) => Promise<void>;
	updateStatus:   (key: string, updater: (prev: GitHubPullRequestStatus | null) => GitHubPullRequestStatus | null) => void;
}

// ── Helpers ─────────────────────────────────────────────────────────────────

export function getGitHubPrStatusKey(directory: string, branch: string, _remoteName?: string | null): string {
	return `${directory}::${branch}`;
}

const isTerminalPrState = (state: string | null | undefined): boolean =>
	state === 'closed' || state === 'merged';

const isPendingChecks = (status: GitHubPullRequestStatus | null): boolean => {
	const checks = status?.checks;
	if (!checks) return false;
	return checks.state === 'pending' || (checks.pending ?? 0) > 0;
};

const createEntry = (): PrStatusEntry => ({
	status:                  null,
	isLoading:               false,
	error:                   null,
	isInitialStatusResolved: false,
	lastRefreshAt:           0,
	lastDiscoveryPollAt:     0,
	watchers:                0,
	params:                  null,
	identity:                null,
	resolvedRemoteName:      null,
});

function getIdentityFromEntry(entry: PrStatusEntry | null | undefined): PrEntryIdentity | null {
	if (entry?.params?.directory && entry.params.branch) {
		return {
			directory:  entry.params.directory,
			branch:     entry.params.branch,
			remoteName: entry.params.remoteName ?? entry.resolvedRemoteName ?? entry.identity?.remoteName ?? null,
		};
	}
	if (!entry?.identity?.directory || !entry.identity.branch) return null;
	return entry.identity;
}

function getSignatureFromEntry(entry: PrStatusEntry | null | undefined): string | null {
	const identity = getIdentityFromEntry(entry);
	if (!identity?.directory || !identity.branch) return null;
	return `${identity.directory}::${identity.branch}`;
}

function getKeysBySignature(entries: Record<string, PrStatusEntry>, signature: string): string[] {
	return Object.entries(entries)
		.filter(([, entry]) => getSignatureFromEntry(entry) === signature)
		.map(([key]) => key);
}

function mergeParams(entry: PrStatusEntry, next: PrRuntimeParams): PrStatusEntry {
	const remoteName = next.remoteName ?? entry.params?.remoteName ?? entry.resolvedRemoteName ?? entry.identity?.remoteName ?? null;
	return {
		...entry,
		params: entry.params
			? { ...entry.params, ...next, remoteName }
			: { ...next, remoteName },
		identity: { directory: next.directory, branch: next.branch, remoteName },
	};
}

function getFetchableParams(entry: PrStatusEntry | null | undefined): PrRuntimeParams | null {
	// In Lis we don't need a runtime bridge — `canShow` alone is the gate.
	if (!entry?.params?.canShow) return null;
	return {
		...entry.params,
		remoteName: entry.params.remoteName ?? entry.resolvedRemoteName ?? entry.identity?.remoteName ?? null,
	};
}

function pickFetchParamsForSignature(
	entries:      Record<string, PrStatusEntry>,
	signature:    string,
	preferredKey: string,
): PrRuntimeParams | null {
	const keys = getKeysBySignature(entries, signature);
	const candidates = keys
		.map(key => getFetchableParams(entries[key]))
		.filter((params): params is PrRuntimeParams => Boolean(params));
	if (candidates.length === 0) return null;

	const preferred = getFetchableParams(entries[preferredKey]);
	if (preferred && getSignatureFromEntry(entries[preferredKey]) === signature) return preferred;

	const withResolvedRemote = keys
		.map(key => entries[key])
		.find(entry => Boolean(entry?.resolvedRemoteName && getFetchableParams(entry)));
	if (withResolvedRemote) return getFetchableParams(withResolvedRemote);

	const withRemote = candidates.find(p => Boolean(p.remoteName));
	if (withRemote) return withRemote;

	return candidates[0] ?? null;
}

// ── Wire shape (must match Lis.Api.Schemas.GitHub.PrStatusDto) ──────────────

interface PrStatusByDirectoryWire {
	results: Record<string, GitHubPullRequestStatus & { connected: boolean }>;
}

async function fetchPrStatusFor(params: PrRuntimeParams): Promise<GitHubPullRequestStatus> {
	// Prefer a runtime bridge if one is provided (kept for OC desktop parity in
	// shared bundles); the VS Code path always falls through to LisRpcClient.
	const bridge = params.github;
	if (bridge && typeof (bridge as { prStatus?: unknown }).prStatus === 'function') {
		const prStatusFn = (bridge as { prStatus: (directory: string, branch: string, remoteName?: string, options?: { force?: boolean }) => Promise<GitHubPullRequestStatus> }).prStatus;
		return prStatusFn(params.directory, params.branch, params.remoteName ?? undefined);
	}

	const payload = await LisRpcClient.instance().getPrStatusByDirectory([
		{ directory: params.directory, branch: params.branch },
	]) as PrStatusByDirectoryWire;

	const key = `${params.directory}::${params.branch}`;
	const result = payload?.results?.[key];
	if (!result) {
		// Backend returned a payload but no row for this key — treat as disconnected so
		// the UI shows its empty state rather than spinning forever.
		return { connected: false };
	}
	return result;
}

// ── Timer registries (module-scoped so they survive component remounts) ────

const timers           = new Map<string, ReturnType<typeof setInterval>>();
const bootstrapTimers  = new Map<string, ReturnType<typeof setTimeout>[]>();
const inFlightBySig    = new Set<string>();
const lastRefreshBySig = new Map<string, number>();

// ── Store ───────────────────────────────────────────────────────────────────

export const useGitHubPrStatusStore = create<GitHubPrStatusStore>((set, get) => ({
	entries:             {},
	activeRequestCount:  0,
	totalRequestCount:   0,

	ensureEntry: (key) => {
		set(state => {
			if (state.entries[key]) return state;
			return { entries: { ...state.entries, [key]: createEntry() } };
		});
	},

	setParams: (key, params) => {
		set(state => {
			const current = state.entries[key] ?? createEntry();
			return { entries: { ...state.entries, [key]: mergeParams(current, params) } };
		});
	},

	startWatching: (key) => {
		set(state => {
			const current = state.entries[key] ?? createEntry();
			return {
				entries: { ...state.entries, [key]: { ...current, watchers: current.watchers + 1 } },
			};
		});

		if (timers.has(key)) return;

		const runBootstrap = (delayMs: number): void => {
			const id = setTimeout(() => {
				if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
				const entry = get().entries[key];
				if (!entry || entry.watchers <= 0) return;
				if (entry.status?.pr) return; // already discovered — don't waste a retry
				void get().refresh(key, { force: true, silent: true, markInitialResolved: true });
			}, delayMs);
			const existing = bootstrapTimers.get(key) ?? [];
			existing.push(id);
			bootstrapTimers.set(key, existing);
		};

		// Immediate refresh + two bootstrap retries (catches the common "user just
		// opened the sidebar but backend is warming up" race).
		void get().refresh(key, { force: true, silent: true, markInitialResolved: true });
		for (const d of PR_BOOTSTRAP_RETRY_DELAYS_MS) runBootstrap(d);

		const intervalId = setInterval(() => {
			if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;

			const entry = get().entries[key];
			if (!entry || entry.watchers <= 0) return;

			const hasPr = Boolean(entry.status?.pr);

			// Discovery loop: no PR resolved yet → poll at PR_DISCOVERY_INTERVAL_MS.
			if (!hasPr) {
				const now = Date.now();
				if (now - entry.lastDiscoveryPollAt < PR_DISCOVERY_INTERVAL_MS) return;
				set(state => {
					const cur = state.entries[key];
					if (!cur) return state;
					return { entries: { ...state.entries, [key]: { ...cur, lastDiscoveryPollAt: now } } };
				});
				void get().refresh(key, { force: true, silent: true, markInitialResolved: true });
				return;
			}

			// Terminal state (merged / closed) → stop polling.
			if (isTerminalPrState(entry.status?.pr?.state)) return;

			// Live PR → adaptive cadence.
			const elapsed = Date.now() - entry.lastRefreshAt;
			const nextInterval = isPendingChecks(entry.status)
				? PR_OPEN_BUSY_INTERVAL_MS
				: entry.status?.checks && entry.status.checks.state !== 'pending'
					? PR_OPEN_STABLE_INTERVAL_MS
					: PR_OPEN_DEFAULT_INTERVAL_MS;
			if (elapsed < nextInterval) return;

			void get().refresh(key, { force: true, onlyExistingPr: true, silent: true, markInitialResolved: true });
		}, PR_REVALIDATE_INTERVAL_MS);

		timers.set(key, intervalId);
	},

	stopWatching: (key) => {
		set(state => {
			const current = state.entries[key];
			if (!current) return state;
			const watchers = Math.max(0, current.watchers - 1);
			return { entries: { ...state.entries, [key]: { ...current, watchers } } };
		});

		const entry = get().entries[key];
		if (entry && entry.watchers > 0) return; // someone else is still watching

		const timerId = timers.get(key);
		if (timerId !== undefined) clearInterval(timerId);
		timers.delete(key);

		const bootstraps = bootstrapTimers.get(key);
		if (bootstraps) for (const id of bootstraps) clearTimeout(id);
		bootstrapTimers.delete(key);
	},

	refresh: async (key, options) => {
		const state = get();
		const entry = state.entries[key];
		const signature = getSignatureFromEntry(entry);
		if (!entry || !signature) return;

		const signatureKeys = getKeysBySignature(state.entries, signature);
		const hasExistingPr = signatureKeys.some(k => Boolean(state.entries[k]?.status?.pr));
		if (options?.onlyExistingPr && !hasExistingPr) return;

		const lastRefreshAt = lastRefreshBySig.get(signature) ?? 0;
		if (!options?.force && Date.now() - lastRefreshAt < PR_REVALIDATE_TTL_MS) return;

		if (inFlightBySig.has(signature)) return;

		const params = pickFetchParamsForSignature(state.entries, signature, key);
		if (!params) return;

		inFlightBySig.add(signature);
		lastRefreshBySig.set(signature, Date.now());

		// Mark every co-signed entry as refreshing.
		set(prev => {
			const nextEntries = { ...prev.entries };
			for (const sk of signatureKeys) {
				const cur = nextEntries[sk];
				if (!cur) continue;
				nextEntries[sk] = {
					...cur,
					lastRefreshAt: Date.now(),
					isLoading: options?.silent ? cur.isLoading : true,
					error:     null,
				};
			}
			return { entries: nextEntries };
		});

		// Short-circuit: known-disconnected GitHub → don't even attempt a fetch.
		if (params.githubAuthChecked && params.githubConnected === false) {
			set(prev => applyToAll(prev, signatureKeys, cur => ({
				...cur,
				status:                  { connected: false },
				error:                   null,
				isLoading:               options?.silent ? cur.isLoading : false,
				isInitialStatusResolved: options?.markInitialResolved === false ? cur.isInitialStatusResolved : true,
			})));
			inFlightBySig.delete(signature);
			return;
		}

		try {
			set(prev => ({
				...prev,
				activeRequestCount: prev.activeRequestCount + 1,
				totalRequestCount:  prev.totalRequestCount + 1,
			}));

			const next = await fetchPrStatusFor(params);

			set(prev => applyToAll(prev, signatureKeys, cur => {
				// Carry over the previously-fetched PR body when the fresh payload omits
				// it (Lis's list-PRs path doesn't include the body; GET-by-number does).
				const prevPr = cur.status?.pr;
				const nextPr = next.pr;
				const shouldCarryBody = Boolean(
					nextPr
					&& prevPr
					&& nextPr.number === prevPr.number
					&& (!nextPr.body || !nextPr.body.trim())
					&& typeof prevPr.body === 'string'
					&& prevPr.body.trim().length > 0,
				);

				const status: GitHubPullRequestStatus = shouldCarryBody && nextPr && prevPr?.body
					? { ...next, pr: { ...nextPr, body: prevPr.body } }
					: next;

				const resolvedRemoteName = status.resolvedRemoteName ?? cur.resolvedRemoteName ?? params.remoteName ?? null;
				const identity = getIdentityFromEntry(cur) ?? {
					directory:  params.directory,
					branch:     params.branch,
					remoteName: params.remoteName ?? null,
				};

				return {
					...cur,
					status,
					error:                   null,
					isLoading:               options?.silent ? cur.isLoading : false,
					isInitialStatusResolved: options?.markInitialResolved === false ? cur.isInitialStatusResolved : true,
					resolvedRemoteName,
					identity: { ...identity, remoteName: resolvedRemoteName ?? identity.remoteName ?? null },
				};
			}));
		} catch (error) {
			const message = error instanceof LisRpcError ? error.message
				:           error instanceof Error        ? error.message
				:           String(error);
			set(prev => applyToAll(prev, signatureKeys, cur => ({
				...cur,
				error:                   message || 'Failed to load PR status',
				isLoading:               options?.silent ? cur.isLoading : false,
				isInitialStatusResolved: options?.markInitialResolved === false ? cur.isInitialStatusResolved : true,
			})));
		} finally {
			inFlightBySig.delete(signature);
			set(prev => ({ ...prev, activeRequestCount: Math.max(0, prev.activeRequestCount - 1) }));
		}
	},

	refreshTargets: async (targets, options) => {
		const keys = Array.from(new Set(
			targets
				.map(t => {
					const dir    = t.directory.trim();
					const branch = t.branch.trim();
					if (!dir || !branch) return null;
					return getGitHubPrStatusKey(dir, branch, t.remoteName ?? null);
				})
				.filter((k): k is string => Boolean(k)),
		));
		await Promise.all(keys.map(k => get().refresh(k, options)));
	},

	updateStatus: (key, updater) => {
		set(state => {
			const current = state.entries[key] ?? createEntry();
			return {
				entries: { ...state.entries, [key]: { ...current, status: updater(current.status) } },
			};
		});
	},
}));

function applyToAll(
	prev:    GitHubPrStatusStore,
	keys:    string[],
	updater: (cur: PrStatusEntry) => PrStatusEntry,
): { entries: Record<string, PrStatusEntry> } {
	const nextEntries = { ...prev.entries };
	for (const k of keys) {
		const cur = nextEntries[k];
		if (!cur) continue;
		nextEntries[k] = updater(cur);
	}
	return { entries: nextEntries };
}

// ── Visual summary hook (memoised, ported from OC) ─────────────────────────

export type PrVisualState = 'draft' | 'open' | 'blocked' | 'merged' | 'closed' | null;

export interface PrVisualSummary {
	number:         number;
	visualState:    Exclude<PrVisualState, null>;
	prState:        string;
	draft:          boolean;
	title:          string | null;
	url:            string | null;
	base:           string | null;
	head:           string | null;
	checks:         { state: string; total: number; success: number; failure: number; pending: number } | null;
	canMerge:       boolean | null;
	mergeableState: string | null;
	repo:           { owner: string; repo: string } | null;
}

function derivePrVisualState(status: GitHubPullRequestStatus | null): PrVisualState {
	const pr = status?.pr;
	if (!pr) return null;
	if (pr.state === 'merged') return 'merged';
	if (pr.state === 'closed') return 'closed';
	if (pr.draft) return 'draft';
	const checksFailed = status?.checks?.state === 'failure';
	const ms = typeof pr.mergeableState === 'string' ? pr.mergeableState : '';
	const notMergeable = pr.mergeable === false || ms === 'blocked' || ms === 'dirty';
	if (checksFailed || notMergeable) return 'blocked';
	return 'open';
}

function deriveSummary(entry: PrStatusEntry): PrVisualSummary | null {
	const vs = derivePrVisualState(entry.status ?? null);
	const pr = entry.status?.pr;
	if (!vs || !pr?.number) return null;
	return {
		number:         pr.number,
		visualState:    vs,
		prState:        pr.state,
		draft:          Boolean(pr.draft),
		title:          typeof pr.title === 'string' && pr.title.trim().length > 0 ? pr.title : null,
		url:            typeof pr.url   === 'string' && pr.url.trim().length   > 0 ? pr.url   : null,
		base:           typeof pr.base  === 'string' && pr.base.trim().length  > 0 ? pr.base  : null,
		head:           typeof pr.head  === 'string' && pr.head.trim().length  > 0 ? pr.head  : null,
		checks:         entry.status?.checks
			? {
				state:   entry.status.checks.state ?? 'pending',
				total:   entry.status.checks.total   ?? 0,
				success: entry.status.checks.success ?? 0,
				failure: entry.status.checks.failure ?? 0,
				pending: entry.status.checks.pending ?? 0,
			  }
			: null,
		canMerge:       typeof entry.status?.canMerge === 'boolean' ? entry.status.canMerge : null,
		mergeableState: typeof pr.mergeableState === 'string' ? pr.mergeableState : null,
		repo:           entry.status?.repo ? { owner: entry.status.repo.owner, repo: entry.status.repo.repo } : null,
	};
}

function summarySignature(s: PrVisualSummary): string {
	return `${s.number}:${s.visualState}:${s.prState}:${s.draft}:${s.title ?? ''}:${s.url ?? ''}:${s.base ?? ''}:${s.head ?? ''}:${s.canMerge ?? ''}:${s.mergeableState ?? ''}:${s.checks?.state ?? ''}:${s.checks?.total ?? ''}:${s.checks?.success ?? ''}:${s.checks?.failure ?? ''}:${s.checks?.pending ?? ''}:${s.repo?.owner ?? ''}:${s.repo?.repo ?? ''}`;
}

let prKeyedCacheSigs:  Map<string, string>          = new Map();
let prKeyedCacheRes:   Map<string, PrVisualSummary> = new Map();

/**
 * Pure derivation of visual summaries from the entries map. Memoised by signature
 * so identical inputs return the same Map reference (lets downstream
 * `React.useMemo`/`useEffect` chains stay stable). Exposed as a plain function so
 * tests can exercise it outside a React render.
 */
export function selectPrVisualSummaries(
	entries: Record<string, PrStatusEntry>,
	keys:    string[] | ReadonlySet<string>,
): Map<string, PrVisualSummary> {
	const keyArr     = keys instanceof Set ? Array.from(keys) : Array.isArray(keys) ? keys : [];
	const nextSigs    = new Map<string, string>();
	const nextSummary = new Map<string, PrVisualSummary>();

	for (const key of keyArr) {
		const entry = entries[key];
		if (!entry) continue;
		const summary = deriveSummary(entry);
		if (!summary) continue;
		nextSigs.set(key, summarySignature(summary));
		nextSummary.set(key, summary);
	}

	if (nextSigs.size === prKeyedCacheSigs.size) {
		let same = true;
		for (const [k, sig] of nextSigs) {
			if (prKeyedCacheSigs.get(k) !== sig) { same = false; break; }
		}
		if (same) return prKeyedCacheRes;
	}

	prKeyedCacheSigs = nextSigs;
	prKeyedCacheRes  = nextSummary;
	return nextSummary;
}

/** React selector hook — thin shell around {@link selectPrVisualSummaries}. */
export function usePrVisualSummaryByKeys(keys: string[] | ReadonlySet<string>): Map<string, PrVisualSummary> {
	return useGitHubPrStatusStore((state) => selectPrVisualSummaries(state.entries, keys));
}

/** Reset the module-scoped memoisation cache. Tests only. */
export function resetPrVisualSummaryCacheForTesting(): void {
	prKeyedCacheSigs = new Map();
	prKeyedCacheRes  = new Map();
}
