/**
 * Webview-side wrappers over the Lis worktree REST endpoints (Stage 5):
 *
 *   GET    /api/worktrees?project_path=
 *   POST   /api/worktrees           body: { project_path, new_path, branch }
 *   DELETE /api/worktrees           query: { project_path, worktree_path, force }
 *
 * Mirrors the OC `worktreeManager.ts` API shape so byte-identical OC components
 * (NewWorktreeDialog, the right-sidebar worktree list, AgentManagerView) drop
 * in unchanged.
 */

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

export interface ProjectRef {
	id:    string;
	path:  string;
	label?: string;
}

export interface WorktreeMetadata {
	path:        string;
	branch?:     string | null;
	head?:       string | null;
	isLocked?:   boolean;
	isPrunable?: boolean;
	createdFromBranch?: string | null;
	kind?:       'standard' | 'detached';
}

export interface WorktreeCreateOptions {
	preferredName?:  string;
	branchName:      string;
	mode?:           'new' | 'existing';
	worktreeName?:   string;
	startRef?:       string;
	setupCommands?:  string[];
}

export interface WorktreeValidation {
	ok:      boolean;
	reason?: string;
}

const EMPTY_WORKTREES: WorktreeMetadata[] = [];

/** Cheap "is this a real-looking branch name" filter. */
function isValidBranchName(name: string): boolean {
	if (!name) return false;
	if (name.length > 200) return false;
	if (/^[-./]/.test(name)) return false;
	if (/\.\./.test(name)) return false;
	return /^[A-Za-z0-9_./-]+$/.test(name);
}

export function getWorktreeManager(): { listProjectWorktrees: typeof listProjectWorktrees; createWorktree: typeof createWorktree; removeProjectWorktree: typeof removeProjectWorktree } {
	return { listProjectWorktrees, createWorktree, removeProjectWorktree };
}

/** List worktrees configured on the project's repo. Returns [] on backend error. */
export async function listProjectWorktrees(project: ProjectRef | string): Promise<WorktreeMetadata[]> {
	const path = typeof project === 'string' ? project : project?.path;
	if (!path) return EMPTY_WORKTREES;
	try {
		const list = await LisRpcClient.instance().listWorktrees(path) as WorktreeMetadata[];
		return Array.isArray(list) ? list : EMPTY_WORKTREES;
	} catch (err) {
		if (err instanceof LisRpcError) return EMPTY_WORKTREES;
		throw err;
	}
}

/** Create a worktree at `newPath` checked out to `branch`. Returns the new metadata or null on failure. */
export async function createWorktree(
	projectPath: string,
	newPath:     string,
	branch:      string,
): Promise<WorktreeMetadata | null> {
	if (!projectPath || !newPath || !branch) return null;
	try {
		return await LisRpcClient.instance().createWorktree(projectPath, newPath, branch) as WorktreeMetadata;
	} catch (err) {
		if (err instanceof LisRpcError) return null;
		throw err;
	}
}

/** Remove the worktree at `worktreePath` from the project's repo. `force=true` skips dirty checks. */
export async function removeProjectWorktree(
	projectPath:   string,
	worktreePath:  string,
	options?:      { force?: boolean },
): Promise<boolean> {
	if (!projectPath || !worktreePath) return false;
	try {
		await LisRpcClient.instance().removeWorktree(projectPath, worktreePath, options?.force === true);
		return true;
	} catch (err) {
		if (err instanceof LisRpcError) return false;
		throw err;
	}
}

/**
 * Validate a proposed worktree create request before the user confirms. Pure —
 * only checks shape, doesn't talk to git. Catches the obvious user-input
 * mistakes (blank name, illegal characters) so the dialog can display a
 * friendly error instead of waiting for the backend's 409.
 */
export function validateWorktreeCreate(opts: WorktreeCreateOptions): WorktreeValidation {
	const branch = (opts?.branchName ?? '').trim();
	if (!branch) return { ok: false, reason: 'Branch name is required' };
	if (!isValidBranchName(branch)) return { ok: false, reason: 'Branch name has illegal characters' };
	if (opts.worktreeName !== undefined && opts.worktreeName.trim() === '') {
		return { ok: false, reason: 'Worktree name cannot be empty' };
	}
	return { ok: true };
}
