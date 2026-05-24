import { create } from 'zustand';
const noop: any = () => {};
export const useGitHubAuthStore = create<any>(() => ({
	isAuthenticated: false, isLoading: false, token: null, username: null,
	status: 'unknown', hasChecked: false,
	refreshStatus: noop, login: noop, logout: noop, setStatus: noop,
}));