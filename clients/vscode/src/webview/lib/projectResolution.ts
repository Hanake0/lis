import { useProjectsStore } from '@/stores/useProjectsStore';

interface ProjectLike { id: string; path: string }

/**
 * Given a session's working directory, find the matching project (if any).
 *
 * Matches both:
 *   1. A project's exact path (the typical case — chat is rooted in the
 *      project directory).
 *   2. A worktree path on disk that belongs to a project (the chat was
 *      opened inside a git worktree of the project — common when working
 *      on multiple branches in parallel).
 *
 * Signature mirrors OC's `resolveProjectForSessionDirectory(projects,
 * availableWorktreesByProject, directory)`. The OC component tree expects
 * this exact arity; a single-arg variant silently bound `projects` to
 * `directory` at runtime and threw `n.trim is not a function` (Array has no
 * `.trim`), wrapping the chat panel in the React error boundary.
 *
 * Returns the project external_id (the `id` Lis serves) when a match is
 * found, otherwise null. `projects` is optional — when undefined we fall
 * back to `useProjectsStore.getState().projects`.
 */
export function resolveProjectForSessionDirectory(
	projectsOrDirectory:           ProjectLike[] | string | null | undefined,
	availableWorktreesByProject?:  Map<string, { path: string }[]> | Record<string, { path: string }[]> | null,
	directory?:                    string | null | undefined,
): string | null {
	// Backwards-compat overload: when called with a single arg, treat it as
	// `directory` and pull the project list from the store. This is what the
	// existing unit test (`projectResolution.test.ts`) relies on.
	let projects: ProjectLike[];
	let dir:      string | null | undefined;
	if (typeof projectsOrDirectory === 'string' || projectsOrDirectory === null || projectsOrDirectory === undefined) {
		projects = useProjectsStore.getState().projects as ProjectLike[];
		dir      = projectsOrDirectory;
	} else {
		projects = projectsOrDirectory;
		dir      = directory;
	}
	if (typeof dir !== 'string' || dir.length === 0) return null;

	const normalized = normalize(dir);
	if (!normalized) return null;

	for (const project of projects) {
		if (typeof project?.path === 'string' && normalize(project.path) === normalized) return project.id;
	}

	// Fall through to the worktree map. Each project may own N worktrees on
	// disk; a session opened in one of them belongs to the owning project.
	if (availableWorktreesByProject) {
		const entries = availableWorktreesByProject instanceof Map
			? Array.from(availableWorktreesByProject.entries())
			: Object.entries(availableWorktreesByProject);
		for (const [projectId, worktrees] of entries) {
			if (!Array.isArray(worktrees)) continue;
			for (const wt of worktrees) {
				if (typeof wt?.path === 'string' && normalize(wt.path) === normalized) return projectId;
			}
		}
	}

	return null;
}

/** Normalise a path for cross-platform comparison: forward slashes, no trailing /. */
function normalize(path: string): string {
	if (typeof path !== 'string') return '';
	const trimmed = path.trim();
	if (!trimmed) return '';
	const slashed = trimmed.replace(/\\/g, '/');
	const noTrail = slashed.length > 1 ? slashed.replace(/\/+$/, '') : slashed;
	// Drive-letter normalisation: lowercase the letter so `C:/Foo` === `c:/Foo` on Windows.
	return noTrail.replace(/^([a-zA-Z]):/, (_, letter: string) => `${letter.toLowerCase()}:`);
}
