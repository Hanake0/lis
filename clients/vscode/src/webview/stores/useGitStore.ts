import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGitStore = create<any>(() => ({
	branches: [], currentBranch: null, isGitRepo: false,
	status: null, log: [], fileCount: 0,
	isLoadingBranches: false, isLoadingStatus: false, isLoadingLog: false,
	repoStatusMap: {}, identity: null, allBranches: [],
	branchLabel: '',
	fetchBranches: asyncNoop, fetchStatus: asyncNoop, fetchLog: asyncNoop,
}));
export function useGitBranches(): string[] { return []; }
export function useIsGitRepo(): boolean { return false; }
export function useGitAllBranches(..._a: any[]): any { return []; }
export function useGitBranchLabel(..._a: any[]): any { return ''; }
export function useGitFileCount(..._a: any[]): any { return 0; }
export function useGitIdentity(..._a: any[]): any { return null; }
export function useGitLoadingBranches(..._a: any[]): any { return false; }
export function useGitLoadingLog(..._a: any[]): any { return false; }
export function useGitLoadingStatus(..._a: any[]): any { return false; }
export function useGitLog(..._a: any[]): any { return []; }
export function useGitRepoStatusMap(..._a: any[]): any { return {}; }
export function useGitStatus(..._a: any[]): any { return null; }