import { create } from 'zustand';

interface GitStore {
	branches: string[];
	currentBranch: string | null;
	isGitRepo: boolean;
}

export const useGitStore = create<GitStore>(() => ({
	branches: [],
	currentBranch: null,
	isGitRepo: false,
}));

export function useGitBranches(): string[] { return []; }
export function useIsGitRepo(): boolean { return false; }
export function useGitAllBranches(..._args: any[]): any { return null; }
export function useGitBranchLabel(..._args: any[]): any { return null; }
export function useGitFileCount(..._args: any[]): any { return null; }
export function useGitIdentity(..._args: any[]): any { return null; }
export function useGitLoadingBranches(..._args: any[]): any { return null; }
export function useGitLoadingLog(..._args: any[]): any { return null; }
export function useGitLoadingStatus(..._args: any[]): any { return null; }
export function useGitLog(..._args: any[]): any { return null; }
export function useGitRepoStatusMap(..._args: any[]): any { return null; }
export function useGitStatus(..._args: any[]): any { return null; }
