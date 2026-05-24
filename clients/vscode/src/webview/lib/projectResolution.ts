import { useProjectsStore } from '@/stores/useProjectsStore';

/**
 * Given a session's working directory, find the matching project (if any).
 * The sidebar uses this to group sessions under their parent project bucket.
 *
 * Match is exact-path (after normalisation). Returns the project external_id
 * (the `id` Lis serves) when a match is found, otherwise null.
 */
export function resolveProjectForSessionDirectory(directory: string | null | undefined): string | null {
	if (!directory) return null;
	const normalized = normalize(directory);
	if (!normalized) return null;

	const { projects } = useProjectsStore.getState();
	for (const project of projects) {
		if (normalize(project.path) === normalized) return project.id;
	}
	return null;
}

/** Normalise a path for cross-platform comparison: forward slashes, no trailing /. */
function normalize(path: string): string {
	const trimmed = path.trim();
	if (!trimmed) return '';
	const slashed = trimmed.replace(/\\/g, '/');
	const noTrail = slashed.length > 1 ? slashed.replace(/\/+$/, '') : slashed;
	// Drive-letter normalisation: lowercase the letter so `C:/Foo` === `c:/Foo` on Windows.
	return noTrail.replace(/^([a-zA-Z]):/, (_, letter: string) => `${letter.toLowerCase()}:`);
}
