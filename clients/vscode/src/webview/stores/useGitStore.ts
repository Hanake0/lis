import { create } from 'zustand';
const noop: any = () => {};
const EMPTY_ARR: any[] = [];
const EMPTY_MAP: Map<string, any> = new Map();
export const useGitStore = create<any>(() => ({
	branches: EMPTY_ARR, currentBranch: null, isGitRepo: false,
	status: null, log: EMPTY_ARR, fileCount: 0, directories: EMPTY_MAP,
	isLoadingBranches: false, isLoadingStatus: false, isLoadingLog: false,
	repoStatusMap: EMPTY_MAP, identity: null, allBranches: EMPTY_ARR, branchLabel: '',
	fetchBranches: noop, fetchStatus: noop, fetchLog: noop,
	ensureStatus: noop, setDiff: noop, setActiveDirectory: noop,
	fetchAll: noop, ensureAll: noop, fetchIdentity: noop, prefetchDiffs: noop,
	setLogMaxCount: noop, moveStatusPathsOptimistically: noop,
	restoreStatus: noop, bumpIndexRevision: noop,
}));
export function useGitBranches(): string[] { return EMPTY_ARR; }
export function useIsGitRepo(): boolean { return false; }
export function useGitAllBranches(): any { return EMPTY_MAP; }
export function useGitBranchLabel(): any { return ''; }
export function useGitFileCount(): any { return 0; }
export function useGitIdentity(): any { return null; }
export function useGitLoadingBranches(): any { return false; }
export function useGitLoadingLog(): any { return false; }
export function useGitLoadingStatus(): any { return false; }
export function useGitLog(): any { return EMPTY_ARR; }
export function useGitRepoStatusMap(): any { return EMPTY_MAP; }
export function useGitStatus(): any { return null; }