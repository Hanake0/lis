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
