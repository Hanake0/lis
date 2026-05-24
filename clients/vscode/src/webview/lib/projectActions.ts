/**
 * Per-project "action" buttons (run dev server, run tests, etc.). OC desktop
 * persists these in a TOML file inside the project root and spawns the
 * configured command on click. Lis doesn't have a backend hook for that yet
 * — the configured commands would have to run on the developer's machine,
 * which means either:
 *
 *   (a) the VS Code extension host shells them out via `child_process.spawn`,
 *   (b) a dedicated Lis backend endpoint with per-project trust grants, or
 *   (c) reuse VS Code's `tasks.json` (the user already has a UI for that).
 *
 * Option (c) is the right answer for Lis but means there's nothing to wire
 * here yet — the sidebar's ProjectActionsButton will render with zero
 * discovered actions, which degrades to an empty menu. We keep the surface
 * typed so the component compiles; the constants here are stable to import.
 */

import type { ProjectEntry } from '@/lib/api/types';

export interface ProjectAction {
	id:                  string;
	label:               string;
	command:             string;
	icon?:               string;
	directory?:          string;
	desktopOpenSshForward?: string;
}

export interface ProjectActionsState {
	actions: ProjectAction[];
	selected?: string | null;
}

export interface ProjectActionRunKey {
	directory: string;
	actionId:  string;
}

const EMPTY_ACTIONS: ProjectAction[] = [];

/** Stable empty list; identity is preserved across calls to keep React memoization stable. */
export function getProjectActions(): ProjectAction[] { return EMPTY_ACTIONS; }

/**
 * Read the per-project actions state. Returns an empty list — Lis doesn't
 * persist per-project actions yet (see file-level comment). Async to mirror
 * OC's signature so consumers don't need to branch.
 */
export async function getProjectActionsState(_project: Pick<ProjectEntry, 'id' | 'path'> | null): Promise<ProjectActionsState> {
	return { actions: EMPTY_ACTIONS, selected: null };
}

/** Persist the per-project actions state. Noop until Lis adds the backend. */
export async function saveProjectActionsState(_project: Pick<ProjectEntry, 'id' | 'path'>, _state: ProjectActionsState): Promise<void> {
	/* deferred */
}

/** Normalise the directory string so equal paths produce equal keys. */
export function normalizeProjectActionDirectory(directory: string | null | undefined): string {
	const trimmed = (directory ?? '').trim();
	if (!trimmed) return '';
	return trimmed.replace(/\\/g, '/').replace(/\/+$/, '');
}

/** Stable string identifier for a (directory, actionId) pair — used as a run-state map key. */
export function toProjectActionRunKey(directory: string, actionId: string): string {
	return `${normalizeProjectActionDirectory(directory)}::${actionId}`;
}

/** OC desktop SSH-forward URL resolver — not applicable to Lis. */
export function resolveProjectActionDesktopForwardUrl(_forward: string | null | undefined, _instances: unknown): string | null { return null; }

/** OC desktop-forward options builder — not applicable to Lis. */
export function buildProjectActionDesktopForwardOptions(): null { return null; }

/** Browser-event name fired when project actions change. */
export const PROJECT_ACTIONS_UPDATED_EVENT = 'lis.project-actions-updated';

/** Icon-key → icon-id map. Same shape as OC's; empty until Lis adds default actions. */
export const PROJECT_ACTION_ICON_MAP: Record<string, string> = {
	play:     'play',
	stop:     'square',
	refresh:  'refresh',
	terminal: 'terminal-box',
	flask:    'flask',
	rocket:   'rocket',
	wrench:   'wrench',
	hammer:   'hammer',
	bug:      'bug',
};

export const PROJECT_ACTION_ICONS: string[] = Object.keys(PROJECT_ACTION_ICON_MAP);
