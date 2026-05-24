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

// ── Pull request shapes (consumed by GitView and the session sidebar) ────────
//
// Lis's `/api/github/pr-status` returns a thin `{ state, checks, reviews }`
// per key — enough to drive a badge but nowhere near the rich shape OC's
// desktop runtime exposes. The types below cover the union: every field is
// optional/nullable so consumers using deep optional chaining keep working
// whether the data came from Lis (sparse) or a future enriched source.

export interface GitHubChecksSummary {
	state?:    'success' | 'failure' | 'pending' | 'neutral' | string | null;
	total?:    number;
	pending?:  number;
	failure?:  number;
	success?:  number;
}

export interface GitHubPullRequest {
	number:           number;
	title:            string;
	body?:            string;
	url:              string;
	state:            'open' | 'closed' | 'merged';
	draft:            boolean;
	base:             string;
	head:             string;
	headSha?:         string;
	mergeable?:       boolean | null;
	mergeableState?:  string | null;
}

export interface GitHubPullRequestStatus {
	connected:           boolean;
	repo?:               { owner: string; repo: string } | null;
	branch?:             string;
	pr?:                 GitHubPullRequest | null;
	checks?:             GitHubChecksSummary | null;
	canMerge?:           boolean;
	defaultBranch?:      string | null;
	resolvedRemoteName?: string | null;
}
