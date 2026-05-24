import { create } from 'zustand';

import type { GitHubPullRequestStatus, RuntimeAPIs } from '@/lib/api/types';

/**
 * Pull-request status tracker for the session sidebar and git view.
 *
 * Lis's `/api/github/pr-status` (Stage 8a) returns a thin `{ state, checks,
 * reviews }` triple per `{owner, repo, branch}` key — useful for a badge,
 * but well short of the rich `GitHubPullRequestStatus` shape OC's desktop
 * runtime exposes (full PR metadata, check runs, merge eligibility, etc.).
 *
 * Rather than ship half-working polling that surfaces stale or partial
 * states, this store is a typed no-op that mirrors OC's surface so the
 * byte-identical consumers (SessionSidebar, PullRequestSection, GitView)
 * compile, but reports "no PR data" everywhere. Badges simply won't appear
 * and the PR section will render its empty state — the accurate position
 * until either:
 *
 *   - the backend grows enriched PR DTOs (Stage 8b in the plan), and
 *   - this store ports the polling/watcher/discovery machinery from OC.
 *
 * Both are deferred per the plan's "Stage 8b deferable" note.
 */

export interface PrStatusEntry {
	status:                  GitHubPullRequestStatus | null;
	isLoading:               boolean;
	error:                   string | null;
	isInitialStatusResolved: boolean;
	lastRefreshAt:           number;
	lastDiscoveryPollAt:     number;
	watchers:                number;
	identity:                { directory: string; branch: string; remoteName: string | null } | null;
	resolvedRemoteName:      string | null;
}

interface PrTrackingTarget {
	directory:   string;
	branch:      string;
	remoteName?: string | null;
}

interface PrRuntimeParams {
	directory:           string;
	branch:              string;
	remoteName:          string | null;
	canShow:             boolean;
	github?:             RuntimeAPIs['github'];
	githubAuthChecked:   boolean;
	githubConnected:     boolean | null;
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

	ensureEntry:        (target: PrTrackingTarget) => void;
	setParams:          (target: PrTrackingTarget, params: PrRuntimeParams) => void;
	startWatching:      (target: PrTrackingTarget) => void;
	stopWatching:       (target: PrTrackingTarget) => void;
	refresh:            (target: PrTrackingTarget, options?: RefreshOptions) => Promise<void>;
	refreshTargets:     (targets: PrTrackingTarget[], options?: RefreshOptions) => Promise<void>;
	updateStatus:       (target: PrTrackingTarget, status: GitHubPullRequestStatus | null) => void;
}

/** Deterministic key for an `{directory, branch}` pair. Mirrors OC's helper. */
export function getGitHubPrStatusKey(directory: string, branch: string, _remoteName?: string | null): string {
	return `${directory}::${branch}`;
}

export const useGitHubPrStatusStore = create<GitHubPrStatusStore>(() => ({
	entries:             {},
	activeRequestCount:  0,
	totalRequestCount:   0,

	ensureEntry:    () => { /* noop until backend grows enriched PR DTOs */ },
	setParams:      () => { /* noop */ },
	startWatching:  () => { /* noop — refcounted poll loop deferred */ },
	stopWatching:   () => { /* noop */ },
	refresh:        async () => { /* noop */ },
	refreshTargets: async () => { /* noop */ },
	updateStatus:   () => { /* noop */ },
}));

/** Visual summary selector consumed by SessionSidebar for compact badge rendering. */
export function usePrVisualSummaryByKeys(_keys: ReadonlySet<string> | string[]): Map<string, 'draft' | 'open' | 'blocked' | 'merged' | 'closed' | null> {
	return EMPTY_VISUAL_SUMMARY;
}

const EMPTY_VISUAL_SUMMARY: Map<string, 'draft' | 'open' | 'blocked' | 'merged' | 'closed' | null> = new Map();
