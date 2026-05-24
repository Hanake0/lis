export interface RuntimeAPIs {
	vscode?: {
		executeCommand: (command: string, ...args: unknown[]) => Promise<void>;
	};
	/**
	 * Optional runtime-side GitHub bridge. Present on the OC desktop runtime
	 * (where the host process owns the GH PAT in keychain); absent in the VS
	 * Code webview, where the store hits the Lis REST surface directly.
	 */
	github?: {
		authStatus: () => Promise<GitHubAuthStatus>;
	};
	/** Monospace font picked up from VS Code editor settings, if available. */
	monoFont?: string;
}

/**
 * Mirror of `Lis.Api.Schemas.GitHub.GitHubStatusDto` with the OC-compatible
 * `user`/`accounts` optional surface bolted on so byte-identical OC consumers
 * (Header.tsx, NewWorktreeDialog) still compile. The Lis backend currently
 * fills only `connected`, `login`, and `scopes`; the rest are left `undefined`
 * and OC's optional-chaining handles them gracefully.
 */
export interface GitHubAuthStatus {
	connected: boolean;
	login?:    string | null;
	scopes?:   string | null;
	error?:    string;

	/** Reserved for parity with OC. Lis backend does not populate these today. */
	user?:     { login?: string; avatarUrl?: string } | null;
	accounts?: Array<{ id: string; login: string; avatarUrl?: string; isActive?: boolean }>;
}
