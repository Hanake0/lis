import { create } from 'zustand';

import { useProjectsStore } from '@/stores/useProjectsStore';
import { getSafeStorage } from './utils/safeStorage';

/**
 * Lis-native current-directory tracking. In OC desktop this drove the whole
 * directory-history UX (back/forward/parent/home) — in the VS Code webview the
 * "directory" is just the active project's path, which already lives in
 * {@link useProjectsStore}. This store subscribes to the projects store and
 * surfaces a single `currentDirectory` slot so the byte-identical OC dialogs
 * (DirectoryExplorerDialog, ScheduledTasksDialog, GitHubIssuePickerDialog,
 * ProjectNotesTodoPanel, SessionSidebar) compile unchanged.
 *
 * `homeDirectory` is currently empty by default — the host can push a value
 * later via a `lis.workspace` postMessage (handled here at module load).
 * Empty home means tilde-expansion in path pickers no-ops, which is the
 * graceful degradation OC chose for non-desktop runtimes.
 *
 * `setDirectory(path)` updates `currentDirectory` in-place and tries to
 * find a matching project so the active-project pointer follows the user's
 * navigation. Used by the sidebar's "switch project" buttons.
 */

interface DirectoryStore {
	currentDirectory: string;
	homeDirectory:    string;
	isHomeReady:      boolean;
	directoryHistory: string[];
	historyIndex:     number;

	setDirectory:    (path: string, options?: { showOverlay?: boolean }) => void;
	synchronizeHomeDirectory: (path: string) => void;
}

const STORAGE_HOME_KEY = 'lis.directory.home';

/** Best-effort: read a persisted home from localStorage; falls back to '' if unavailable. */
function readPersistedHome(): string {
	try {
		const storage = getSafeStorage();
		const raw = storage?.getItem(STORAGE_HOME_KEY);
		return typeof raw === 'string' ? raw : '';
	} catch {
		return '';
	}
}

const initialHome = readPersistedHome();

export const useDirectoryStore = create<DirectoryStore>((set, get) => ({
	currentDirectory: useProjectsStore.getState().getActiveProject()?.path ?? '',
	homeDirectory:    initialHome,
	isHomeReady:      initialHome.length > 0,
	directoryHistory: [],
	historyIndex:     -1,

	setDirectory: (path, _options) => {
		const trimmed = (path ?? '').trim();
		if (!trimmed) return;
		const prev = get().currentDirectory;
		if (prev === trimmed) return;

		// Push the previous value onto the history so back/forward could be wired
		// in a follow-up — today consumers only call `setDirectory(absolute)`.
		const history = get().directoryHistory.slice(0, get().historyIndex + 1);
		history.push(trimmed);
		set({
			currentDirectory: trimmed,
			directoryHistory: history,
			historyIndex:     history.length - 1,
		});

		// If a project with this exact path exists, follow it so the active project
		// stays in sync with the sidebar's directory.
		const projects = useProjectsStore.getState().projects;
		const match    = projects.find(p => p.path === trimmed);
		if (match) useProjectsStore.getState().setActiveProject(match.id);
	},

	synchronizeHomeDirectory: (path) => {
		const trimmed = (path ?? '').trim();
		if (!trimmed || trimmed === get().homeDirectory) return;
		set({ homeDirectory: trimmed, isHomeReady: true });
		try { getSafeStorage()?.setItem(STORAGE_HOME_KEY, trimmed); } catch { /* swallow */ }
	},
}));

// ── Subscribe to projects store: currentDirectory follows activeProject ────

useProjectsStore.subscribe((state, prev) => {
	if (state.activeProjectId === prev.activeProjectId) return;
	const active = state.projects.find(p => p.id === state.activeProjectId);
	const nextPath = active?.path ?? '';
	if (useDirectoryStore.getState().currentDirectory !== nextPath) {
		useDirectoryStore.setState({ currentDirectory: nextPath });
	}
});

// ── Host workspace injection ────────────────────────────────────────

interface WorkspaceMessage {
	type:           'lis.workspace';
	workspaceRoot?: string;
	homeDirectory?: string;
}

function handleHostMessage(data: unknown): void {
	if (!data || typeof data !== 'object' || (data as { type?: unknown }).type !== 'lis.workspace') return;
	const msg = data as WorkspaceMessage;
	if (msg.homeDirectory) useDirectoryStore.getState().synchronizeHomeDirectory(msg.homeDirectory);
	if (msg.workspaceRoot && !useDirectoryStore.getState().currentDirectory) {
		useDirectoryStore.setState({ currentDirectory: msg.workspaceRoot });
	}
}

if (typeof window !== 'undefined') {
	window.addEventListener('message', (e: MessageEvent) => handleHostMessage(e.data));
}
