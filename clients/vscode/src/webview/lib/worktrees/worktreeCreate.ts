/**
 * "Create-with-defaults" helpers — figure out a sensible path / branch / start-ref
 * for a new worktree under a project, then call the real
 * {@link createWorktree} from `worktreeManager.ts`. Used by NewWorktreeDialog and
 * by the multi-run launcher when isolation is enabled.
 */

import { createWorktree, type ProjectRef, type WorktreeCreateOptions, type WorktreeMetadata } from './worktreeManager';
import { getRootBranch } from './worktreeStatus';

export interface CreateWithDefaultsContext {
	/** Tracking remote resolved from the root branch — used to set the new branch's upstream. */
	resolvedRootTrackingRemote?: string | null;
}

/**
 * Compute defaults for a new worktree and create it. Returns the created
 * worktree's metadata, or throws on backend error (the caller surfaces it
 * via toast).
 */
export async function createWorktreeWithDefaults(
	project: ProjectRef,
	opts:    WorktreeCreateOptions,
	_ctx?:   CreateWithDefaultsContext,
): Promise<WorktreeMetadata> {
	if (!project?.path) throw new Error('createWorktreeWithDefaults: missing project.path');
	const branchName  = (opts.branchName ?? '').trim();
	if (!branchName)  throw new Error('createWorktreeWithDefaults: missing branchName');

	const worktreeName = (opts.worktreeName ?? opts.preferredName ?? branchName).trim();
	const safe         = worktreeName.replace(/[\\/]+/g, '-').replace(/[^A-Za-z0-9_.-]+/g, '-');

	const sep         = project.path.includes('\\') && !project.path.includes('/') ? '\\' : '/';
	const baseClean   = project.path.replace(/[\\/]+$/, '');
	const newPath     = `${baseClean}${sep}.worktrees${sep}${safe}`;

	const created = await createWorktree(project.path, newPath, branchName);
	if (!created) throw new Error(`Failed to create worktree at ${newPath}`);
	return created;
}

/**
 * Layer "upstream" defaults onto a `WorktreeCreateOptions` bag: figure out
 * the start-ref (the current root branch when the caller didn't specify
 * `startRef`). Mirrors OC's same-named helper.
 */
export async function withWorktreeUpstreamDefaults(
	project: ProjectRef,
	opts:    WorktreeCreateOptions,
): Promise<WorktreeCreateOptions> {
	const startRef = opts.startRef ?? await getRootBranch(project.path) ?? 'HEAD';
	return { ...opts, startRef };
}

/**
 * Resolve the tracking remote (`refs/remotes/<name>/<branch>`) for the current
 * root branch. The Lis backend doesn't expose this directly yet — return null
 * so the dialog can fall back to "no upstream" behaviour.
 */
export async function resolveRootTrackingRemote(_projectPath: string): Promise<string | null> {
	return null;
}
