import { create } from 'zustand';
const noop: any = () => {};
export const useGitStore = create<any>(() => ({
	branches: [], currentBranch: null, isGitRepo: false,
	status: null, log: [], fileCount: 0, directories: [],
	isLoadingBranches: false, isLoadingStatus: false, isLoadingLog: false,
	repoStatusMap: {}, identity: null, allBranches: [], branchLabel: '',
	fetchBranches: noop, fetchStatus: noop, fetchLog: noop,
	ensureStatus: noop, setDiff: noop, setActiveDirectory: noop,
	fetchAll: noop, ensureAll: noop, fetchIdentity: noop, prefetchDiffs: noop,
	setLogMaxCount: noop, moveStatusPathsOptimistically: noop,
	restoreStatus: noop, bumpIndexRevision: noop,
}));
export function useGitBranches(): string[] { return []; }
export function useIsGitRepo(): boolean { return false; }
export function useGitAllBranches(): any { return []; }
export function useGitBranchLabel(): any { return ''; }
export function useGitFileCount(): any { return 0; }
export function useGitIdentity(): any { return null; }
export function useGitLoadingBranches(): any { return false; }
export function useGitLoadingLog(): any { return false; }
export function useGitLoadingStatus(): any { return false; }
export function useGitLog(): any { return []; }
export function useGitRepoStatusMap(): any { return {}; }
export function useGitStatus(): any { return null; }