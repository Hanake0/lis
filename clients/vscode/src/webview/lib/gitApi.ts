/**
 * Webview-side wrappers over the Lis git backend. Today the backend exposes
 * `GET /api/git/status?path=` and `GET /api/git/branches?path=` (Stage 5);
 * those are wired here through {@link LisRpcClient}. The rest of OC's gitApi
 * (stash, branch CRUD, commit message generation, diff retrieval, conflict
 * resolution) requires backend endpoints that don't exist yet — those are
 * typed stubs with explicit "not wired" semantics so OC components compile.
 */

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

// ── Wire types ──────────────────────────────────────────────────────

interface RepoStatusDto {
	is_git_repo:  boolean;
	branch?:      string | null;
	ahead?:       number;
	behind?:      number;
	dirty?:       boolean;
}

interface BranchesDto {
	local:    string[];
	remote:   string[];
	current:  string | null;
}

// ── Public types (OC parity) ────────────────────────────────────────

export interface GitRemote {
	name: string;
	url:  string;
}

export interface MergeConflictDetails {
	path:    string;
	ours:    string;
	theirs:  string;
	base?:   string;
}

export interface CommitFileDiffResponse {
	path:        string;
	patch:       string;
	additions:   number;
	deletions:   number;
	binary:      boolean;
}

export interface GitStashEntry {
	index:    number;
	message:  string;
	at:       string;
	files?:   string[];
}

// ── Wired surface ───────────────────────────────────────────────────

/**
 * `true` iff the path is the root of a git repo. Returns `false` on any
 * backend error (the most common "I'm not a repo" signal is a 404 / 400).
 */
export async function checkIsGitRepository(path: string): Promise<boolean> {
	const trimmed = path?.trim();
	if (!trimmed) return false;
	try {
		const dto = await LisRpcClient.instance().getRepoStatus(trimmed) as RepoStatusDto;
		return dto?.is_git_repo === true;
	} catch (err) {
		if (err instanceof LisRpcError) return false;
		throw err;
	}
}

/** Local + remote branches for the given repo. Returns `null` on failure. */
export async function getGitBranches(path: string): Promise<BranchesDto | null> {
	const trimmed = path?.trim();
	if (!trimmed) return null;
	try {
		return await LisRpcClient.instance().listBranches(trimmed) as BranchesDto;
	} catch (err) {
		if (err instanceof LisRpcError) return null;
		throw err;
	}
}

/** Re-export of LisRpcClient under the OC alias to keep consumer imports stable. */
export const git = {
	status:   (path: string) => LisRpcClient.instance().getRepoStatus(path),
	branches: (path: string) => LisRpcClient.instance().listBranches(path),
};

/** Get the git API root object — OC parity for components that just probe for availability. */
export function getGitApi(): typeof git { return git; }

// ── Stub surface (backend not wired yet) ────────────────────────────

function notWired<T>(fn: string, value: T): T {
	if (typeof console !== 'undefined' && console.debug) console.debug(`[gitApi] ${fn} not wired yet — returning fallback`);
	return value;
}

/** Delete a local branch. Backend doesn't expose this yet. */
export async function deleteGitBranch(_path: string, _branch: string, _opts?: { force?: boolean }): Promise<boolean> {
	return notWired('deleteGitBranch', false);
}

/** Rename a branch. Backend doesn't expose this yet. */
export async function renameBranch(_path: string, _oldName: string, _newName: string): Promise<boolean> {
	return notWired('renameBranch', false);
}

/** Stash management — none of these are wired yet. */
export async function listGitStashes  (_path: string): Promise<GitStashEntry[]>   { return notWired('listGitStashes',   []); }
export async function applyGitStash   (_path: string, _index: number): Promise<boolean> { return notWired('applyGitStash',  false); }
export async function popGitStash     (_path: string, _index: number): Promise<boolean> { return notWired('popGitStash',    false); }
export async function dropGitStash    (_path: string, _index: number): Promise<boolean> { return notWired('dropGitStash',   false); }
export async function stashGitChanges (_path: string, _message?: string): Promise<boolean> { return notWired('stashGitChanges', false); }
export async function countGitStashFiles (_path: string, _index: number): Promise<number>  { return notWired('countGitStashFiles', 0); }

/** AI-generated commit message — not wired (would need a backend endpoint). */
export async function generateCommitMessage(_path: string, _opts?: { agent?: string }): Promise<string | null> {
	return notWired('generateCommitMessage', null);
}

/** AI-generated PR description — not wired. */
export async function generatePullRequestDescription(_path: string, _opts?: { agent?: string }): Promise<string | null> {
	return notWired('generatePullRequestDescription', null);
}

/** Per-file diff for a commit — backend exposes no /api/git/diff endpoint yet. */
export async function getCommitFileDiff(_path: string, _commit: string, _file: string): Promise<CommitFileDiffResponse | null> {
	return notWired('getCommitFileDiff', null);
}

/** Inspect a merge-conflicted file. Not wired. */
export async function getConflictDetails(_path: string, _file: string): Promise<MergeConflictDetails | null> {
	return notWired('getConflictDetails', null);
}

/** Worktree bootstrap status (setup commands progress). Not wired yet. */
export async function getGitWorktreeBootstrapStatus(_worktreePath: string): Promise<{ running: boolean; output?: string } | null> {
	return notWired('getGitWorktreeBootstrapStatus', null);
}
