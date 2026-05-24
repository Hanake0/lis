/**
 * Worktree status helpers — wraps the same `/api/git/status` + `/api/git/branches`
 * endpoints `useGitStore` consumes, but for an individual *worktree path* rather
 * than the root repo. The Lis backend treats both the same way (LibGit2Sharp
 * just discovers the repo from whatever path is passed in), so these are thin
 * adapters that share the rpc client cache.
 */

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

interface RepoStatusDto {
	is_git_repo:   boolean;
	branch?:       string | null;
	ahead?:        number;
	behind?:       number;
	dirty?:        boolean;
}

interface BranchesDto {
	local:    string[];
	remote:   string[];
	current:  string | null;
}

export interface WorktreeStatusSummary {
	branch?:  string | null;
	ahead?:   number;
	behind?:  number;
	dirty?:   boolean;
}

const EMPTY: WorktreeStatusSummary = {};

/** Snapshot of `{branch, ahead/behind, dirty}` for a worktree. Empty object on failure. */
export async function getWorktreeStatus(worktreePath: string): Promise<WorktreeStatusSummary> {
	const path = worktreePath?.trim();
	if (!path) return EMPTY;
	try {
		const dto = await LisRpcClient.instance().getRepoStatus(path) as RepoStatusDto;
		if (!dto?.is_git_repo) return EMPTY;
		return { branch: dto.branch, ahead: dto.ahead, behind: dto.behind, dirty: dto.dirty };
	} catch (err) {
		if (err instanceof LisRpcError) return EMPTY;
		throw err;
	}
}

/**
 * Resolve the "root branch" of the repo at `projectPath` — i.e. the branch
 * the repo is currently checked out on (or the first local branch if none).
 * Used by NewWorktreeDialog as the default base ref. Returns null on failure.
 */
export async function getRootBranch(projectPath: string): Promise<string | null> {
	const path = projectPath?.trim();
	if (!path) return null;
	try {
		const dto = await LisRpcClient.instance().listBranches(path) as BranchesDto;
		if (dto.current && dto.current.length > 0) return dto.current;
		if (dto.local.length > 0) return dto.local[0];
		return null;
	} catch (err) {
		if (err instanceof LisRpcError) return null;
		throw err;
	}
}
