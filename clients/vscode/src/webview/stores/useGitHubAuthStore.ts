import { create } from 'zustand';

interface GitHubAuthStore {
	isAuthenticated: boolean;
	refreshStatus: (github: unknown, opts?: { force?: boolean }) => Promise<void>;
}

export const useGitHubAuthStore = create<GitHubAuthStore>((_set) => ({
	isAuthenticated: false,
	refreshStatus: async () => {},
}));
