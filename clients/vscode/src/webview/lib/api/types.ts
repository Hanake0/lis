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

// ── ProjectEntry — what the Lis backend serves under /api/projects ─────────

export interface ProjectEntry {
	id:                     string;
	path:                   string;
	label:                  string | null;
	icon?:                  string | null;
	icon_image_mime?:       string | null;
	icon_image_source?:     string | null;
	icon_image_updated_at?: string | null;
	icon_background?:       string | null;
	color?:                 string | null;
	added_at:               string;
	last_opened_at?:        string | null;
	created_at:             string;
	updated_at:             string;
	sidebar_collapsed:      boolean;
	sort_index:             number;
	iconImage?: { updatedAt?: number; mime?: string };  // OC-shaped fallback for byte-identical components
}

// ── Git ────────────────────────────────────────────────────────────────────

export interface GitBranch {
	name:      string;
	isCurrent?: boolean;
	isRemote?: boolean;
	upstream?: string | null;
	ahead?:    number;
	behind?:   number;
	lastCommitDate?: string;
}

export interface GitStatus {
	branch?:      string | null;
	ahead?:       number;
	behind?:      number;
	dirty?:       boolean;
	files?:       Array<{ path: string; status: string }>;
	isGitRepo?:   boolean;
}

export interface GitRemote {
	name: string;
	url:  string;
}

export interface GitStashEntry {
	index:   number;
	message: string;
	at:      string;
}

export interface GitWorktreeInfo {
	path:       string;
	branch?:    string | null;
	head?:      string | null;
	isLocked?:  boolean;
}

export interface GitLogEntry {
	sha:      string;
	message:  string;
	author:   string;
	date:     string;
	parents?: string[];
}

export interface CommitFileEntry {
	path:      string;
	status:    'added' | 'modified' | 'deleted' | 'renamed' | string;
	additions: number;
	deletions: number;
	oldPath?:  string;
}

export interface GitMergeInProgress {
	heads:    string[];
	message?: string;
}

export interface GitRebaseInProgress {
	onto:    string;
	current: string;
	step:    number;
	total:   number;
}

export interface GitIdentityProfile {
	id:           string;
	name:         string;
	userName:     string;
	userEmail:    string;
	authType?:    'ssh' | 'token' | 'password';
	sshKey?:      string;
	host?:        string;
	color?:       string;
	icon?:        string;
}

// ── GitHub — issues + PR list + repo selector ───────────────────────────────

export interface GitHubIssue {
	number:    number;
	title:     string;
	body?:     string;
	state:     'open' | 'closed';
	url:       string;
	labels?:   string[];
	assignees?: string[];
	createdAt: string;
	updatedAt: string;
}

export interface GitHubIssueComment {
	id:        number;
	body:      string;
	author:    string;
	createdAt: string;
}

export interface GitHubIssueSummary {
	number: number;
	title:  string;
	state:  'open' | 'closed';
	url:    string;
}

export interface GitHubIssuesListResult {
	connected:  boolean;
	repo?:      { owner: string; repo: string };
	issues:     GitHubIssueSummary[];
	hasMore?:   boolean;
	error?:     string;
}

export interface GitHubPullRequestSummary {
	number: number;
	title:  string;
	state:  'open' | 'closed' | 'merged';
	url:    string;
	head:   string;
	base:   string;
}

export interface GitHubPullRequestsListResult {
	connected:  boolean;
	repo?:      { owner: string; repo: string };
	pulls:      GitHubPullRequestSummary[];
	hasMore?:   boolean;
	error?:     string;
}

export interface GitHubPullRequestContextResult {
	connected: boolean;
	pr?:       GitHubPullRequest | null;
	commits?:  GitLogEntry[];
	files?:    CommitFileEntry[];
	error?:    string;
}

export interface GitHubRepoSelector {
	owner: string;
	repo:  string;
}

// ── Editor + terminal + skills ─────────────────────────────────────────────

export interface EditorAPI {
	openFile?: (path: string, options?: { line?: number; column?: number }) => Promise<void>;
	revealRange?: (path: string, range: { start: number; end: number }) => Promise<void>;
}

export interface TerminalStreamEvent {
	type:        'data' | 'exit' | 'error';
	data?:       string;
	exitCode?:   number;
	signal?:     string;
}

export interface SkillsCatalogItem {
	id:           string;
	name:         string;
	description?: string;
	repo?:        string;
	stars?:       number;
	updatedAt?:   string;
}
