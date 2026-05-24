import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGitHubAuthStore = create<any>(() => ({
	isAuthenticated: false, isLoading: false, token: null, username: null,
	refreshStatus: asyncNoop, login: asyncNoop, logout: asyncNoop,
}));