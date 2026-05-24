/**
 * Per-project notes / todos / worktree setup / plan-file helpers. Backed by
 * the Lis `/api/projects/{id}/metadata` KV endpoint (Stage 11). Mirrors the
 * OC `openchamberConfig.ts` surface so the sidebar dialogs (notes panel,
 * NewWorktreeDialog, plan view) drop in unchanged.
 *
 * The metadata `value` column is `jsonb` on Postgres — so every read/write
 * round-trips through `JSON.stringify` / `JSON.parse` here. Strings store
 * as JSON string scalars (`"hello"`), arrays as JSON arrays, etc. The
 * backend validates the payload is well-formed JSON before insert.
 *
 * Each "kind" of metadata is one key in the project's KV bag:
 *   - `notes`         → markdown body (JSON string scalar)
 *   - `todos`         → JSON array of TodoItem
 *   - `setupCommands` → JSON array of string
 *   - `plan`          → markdown body of the plan file (JSON string scalar)
 *
 * The values are persisted on the Lis backend (Postgres) — so a webview
 * reload doesn't lose them and they're available across sidebar instances.
 */

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

// ── OC-compatible constants ─────────────────────────────────────────

export const OPENCHAMBER_PROJECT_NOTES_MAX_LENGTH       = 10_000;
export const OPENCHAMBER_PROJECT_TODO_TEXT_MAX_LENGTH   = 10_000;

const KEY_NOTES          = 'notes';
const KEY_TODOS          = 'todos';
const KEY_SETUP_COMMANDS = 'setupCommands';
const KEY_PLAN           = 'plan';

// ── Types ───────────────────────────────────────────────────────────

export interface ProjectRef { id: string; path?: string }

export interface ProjectTodoItem {
	id:        string;
	text:      string;
	completed: boolean;
	createdAt: string;
}

export interface ProjectContextData {
	notes:  string;
	todos:  ProjectTodoItem[];
}

export interface ProjectPlanFile {
	body:       string;
	updatedAt?: string;
}

// ── Helpers ─────────────────────────────────────────────────────────

async function safeGet<T>(projectId: string, key: string, fallback: T, parse: (raw: string) => T): Promise<T> {
	try {
		const res = await LisRpcClient.instance().getProjectMetadata(projectId, key);
		if (!res.found || res.value == null) return fallback;
		try { return parse(res.value); }
		catch { return fallback; }
	} catch (err) {
		if (err instanceof LisRpcError) return fallback;
		throw err;
	}
}

async function safeSet(projectId: string, key: string, value: string): Promise<boolean> {
	try {
		await LisRpcClient.instance().setProjectMetadata(projectId, key, value);
		return true;
	} catch (err) {
		if (err instanceof LisRpcError) return false;
		throw err;
	}
}

async function safeDelete(projectId: string, key: string): Promise<boolean> {
	try {
		await LisRpcClient.instance().deleteProjectMetadata(projectId, key);
		return true;
	} catch (err) {
		if (err instanceof LisRpcError) return false;
		throw err;
	}
}

function ensureProjectId(project: ProjectRef | string | null | undefined): string | null {
	if (!project) return null;
	if (typeof project === 'string') return project.trim() || null;
	return project.id?.trim() || null;
}

// ── Public API ──────────────────────────────────────────────────────

/** Decode a stored JSON string scalar back to a plain string. Empty when malformed. */
function parseJsonString(raw: string): string {
	const parsed: unknown = JSON.parse(raw);
	return typeof parsed === 'string' ? parsed : '';
}

/** Read the per-project notes + todos pair. */
export async function getProjectNotesAndTodos(project: ProjectRef | string): Promise<ProjectContextData> {
	const id = ensureProjectId(project);
	if (!id) return { notes: '', todos: [] };

	const [notes, todos] = await Promise.all([
		safeGet(id, KEY_NOTES, '', parseJsonString),
		safeGet(id, KEY_TODOS, [] as ProjectTodoItem[], (raw) => {
			const parsed: unknown = JSON.parse(raw);
			return Array.isArray(parsed) ? parsed as ProjectTodoItem[] : [];
		}),
	]);
	return { notes, todos };
}

/** Alias: same as {@link getProjectNotesAndTodos}, kept for OC parity. */
export const getProjectContextData = getProjectNotesAndTodos;

/** Save both notes + todos atomically (two PUTs; failures are reported per-key). */
export async function saveProjectNotesAndTodos(project: ProjectRef | string, data: Partial<ProjectContextData>): Promise<{ ok: boolean; failed?: string[] }> {
	const id = ensureProjectId(project);
	if (!id) return { ok: false, failed: ['no project id'] };

	const failed: string[] = [];
	if (typeof data.notes === 'string') {
		if (data.notes.length > OPENCHAMBER_PROJECT_NOTES_MAX_LENGTH) return { ok: false, failed: ['notes-too-long'] };
		const ok = await safeSet(id, KEY_NOTES, JSON.stringify(data.notes));
		if (!ok) failed.push(KEY_NOTES);
	}
	if (Array.isArray(data.todos)) {
		const ok = await safeSet(id, KEY_TODOS, JSON.stringify(data.todos));
		if (!ok) failed.push(KEY_TODOS);
	}
	return failed.length === 0 ? { ok: true } : { ok: false, failed };
}

/** Read the persisted worktree setup commands (one per line). Empty array when unset. */
export async function getWorktreeSetupCommands(project: ProjectRef | string): Promise<string[]> {
	const id = ensureProjectId(project);
	if (!id) return [];
	return safeGet(id, KEY_SETUP_COMMANDS, [] as string[], (raw) => {
		const parsed: unknown = JSON.parse(raw);
		if (!Array.isArray(parsed)) return [];
		return parsed.filter((c): c is string => typeof c === 'string');
	});
}

/** Persist the worktree setup commands for the project. */
export async function saveWorktreeSetupCommands(project: ProjectRef | string, commands: string[]): Promise<boolean> {
	const id = ensureProjectId(project);
	if (!id) return false;
	return safeSet(id, KEY_SETUP_COMMANDS, JSON.stringify(commands));
}

/** Read the project plan markdown body (and last-updated timestamp). */
export async function readProjectPlanFile(project: ProjectRef | string): Promise<ProjectPlanFile | null> {
	const id = ensureProjectId(project);
	if (!id) return null;
	try {
		const res = await LisRpcClient.instance().getProjectMetadata(id, KEY_PLAN);
		if (!res.found || res.value == null) return null;
		const body = parseJsonString(res.value);
		return { body, updatedAt: res.updated_at ?? undefined };
	} catch (err) {
		if (err instanceof LisRpcError) return null;
		throw err;
	}
}

/** Create or overwrite the plan file. */
export async function createProjectPlanFile(project: ProjectRef | string, body: string): Promise<ProjectPlanFile | null> {
	const id = ensureProjectId(project);
	if (!id) return null;
	const ok = await safeSet(id, KEY_PLAN, JSON.stringify(body));
	if (!ok) return null;
	return readProjectPlanFile(project);
}

/** Same as {@link createProjectPlanFile} — OC's `importProjectPlanFileFromContent` is a paste-in. */
export const importProjectPlanFileFromContent = createProjectPlanFile;

/** Clear the plan file. */
export async function deleteProjectPlanFile(project: ProjectRef | string): Promise<boolean> {
	const id = ensureProjectId(project);
	if (!id) return false;
	return safeDelete(id, KEY_PLAN);
}

/** Pure: parse a plan markdown body into a normalised structure. Used by the plan view. */
export function parseProjectPlanMarkdown(body: string): { sections: Array<{ heading: string; content: string }> } {
	if (typeof body !== 'string' || body.trim().length === 0) return { sections: [] };
	const lines    = body.split(/\r?\n/);
	const sections: Array<{ heading: string; content: string }> = [];
	let current: { heading: string; content: string[] } | null = null;

	for (const line of lines) {
		const headingMatch = /^#{1,3}\s+(.+)$/.exec(line);
		if (headingMatch) {
			if (current) sections.push({ heading: current.heading, content: current.content.join('\n').trim() });
			current = { heading: headingMatch[1].trim(), content: [] };
		} else if (current) {
			current.content.push(line);
		}
	}
	if (current) sections.push({ heading: current.heading, content: current.content.join('\n').trim() });
	return { sections };
}

/** OC global config bag. Lis has nothing to inject here today. */
export function getOpenchamberConfig(): Record<string, unknown> { return {}; }

// ── Re-exports from projectActions for OC import-path compatibility ────
// OC originally bundled the project-actions helpers into openchamberConfig.ts;
// Lis split them out into projectActions.ts but consumers still import the
// names from here. Re-export so both paths resolve.
export {
	getProjectActions,
	getProjectActionsState,
	saveProjectActionsState,
	normalizeProjectActionDirectory,
	toProjectActionRunKey,
	resolveProjectActionDesktopForwardUrl,
	buildProjectActionDesktopForwardOptions,
	PROJECT_ACTIONS_UPDATED_EVENT,
	PROJECT_ACTION_ICON_MAP,
	PROJECT_ACTION_ICONS,
} from './projectActions';
