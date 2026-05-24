import { create } from 'zustand';

import type { GitHubAuthStatus, RuntimeAPIs } from '@/lib/api/types';
import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * GitHub identity store. Reads `GET /api/github/status` via {@link LisRpcClient}
 * and exposes the connected/login/scopes triple (plus an optional `error` slot
 * so Header.tsx and friends can render failure states inline).
 *
 * Mirrors OC's `useGitHubAuthStore` surface so byte-identical OC components
 * keep compiling. The OC desktop runtime exposed a `runtimeGitHub.authStatus`
 * bridge (keychain-backed PAT); in VS Code we go straight to Lis REST.
 */

interface GitHubAuthStore {
	status:     GitHubAuthStatus | null;
	isLoading:  boolean;
	hasChecked: boolean;

	setStatus:     (status: GitHubAuthStatus | null) => void;
	refreshStatus: (runtimeGitHub?: RuntimeAPIs['github'], options?: { force?: boolean }) => Promise<GitHubAuthStatus | null>;
}

// Module-scoped in-flight dedup. See useGlobalSessionsStore for the rationale
// behind cleaning up in the outer async's finally rather than an inner IIFE.
let inflightRefresh: Promise<GitHubAuthStatus | null> | null = null;

async function fetchStatus(runtimeGitHub?: RuntimeAPIs['github']): Promise<GitHubAuthStatus> {
	if (runtimeGitHub) {
		const payload = await runtimeGitHub.authStatus();
		return payload;
	}
	return await LisRpcClient.instance().getGitHubStatus() as GitHubAuthStatus;
}

export const useGitHubAuthStore = create<GitHubAuthStore>((set, get) => ({
	status:     null,
	isLoading:  false,
	hasChecked: false,

	setStatus: (status) => set({ status, hasChecked: true }),

	refreshStatus: async (runtimeGitHub, options) => {
		const { hasChecked, status } = get();
		if (hasChecked && !options?.force) return status;
		if (inflightRefresh)               return inflightRefresh;

		set({ isLoading: true });

		const promise = (async (): Promise<GitHubAuthStatus | null> => {
			try {
				const payload = await fetchStatus(runtimeGitHub);
				set({ status: payload, isLoading: false, hasChecked: true });
				return payload;
			} catch (err) {
				const message = err instanceof LisRpcError ? err.message
					:           err instanceof Error        ? err.message
					:           String(err);
				set({
					status:     { connected: false, error: message },
					isLoading:  false,
					hasChecked: true,
				});
				return null;
			}
		})();

		inflightRefresh = promise;
		try     { return await promise; }
		finally { if (inflightRefresh === promise) inflightRefresh = null; }
	},
}));
