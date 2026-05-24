/**
 * Persist + recall the last working directory across webview reloads. Backed
 * by `localStorage` under `lis.directory.last` — useDirectoryStore reads this
 * on boot, components write it whenever the user navigates to a new project.
 */

const LAST_DIRECTORY_KEY = 'lis.directory.last';

function storage(): Storage | null {
	try { return typeof window !== 'undefined' ? window.localStorage : null; } catch { return null; }
}

export function loadPersistedDirectory(): string | null {
	const raw = storage()?.getItem(LAST_DIRECTORY_KEY);
	return typeof raw === 'string' && raw.length > 0 ? raw : null;
}

export function persistDirectory(directory: string): void {
	const trimmed = directory?.trim() ?? '';
	if (!trimmed) {
		try { storage()?.removeItem(LAST_DIRECTORY_KEY); } catch { /* swallow */ }
		return;
	}
	try { storage()?.setItem(LAST_DIRECTORY_KEY, trimmed); } catch { /* swallow */ }
}
