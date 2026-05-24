/**
 * Webview-side wrappers over the Lis git backend. Every operation here calls a
 * concrete Lis REST endpoint via {@link LisRpcClient} — there are no stubs.
 *
 * Method shapes mirror what OC's `GitAPI` exposes so the byte-identical
 * components (GitView, BranchPickerDialog, StashesDialog, ConflictDialog,
 * HistoryCommitRow, PullRequestSection) drop in unchanged.
 *
 * Surfaces wired here:
 *   - branch CRUD (delete with not-fully-merged refusal, rename)
 *   - stash list/save/apply/pop/drop/countFiles
 *   - per-commit per-file diff (Monaco-ready original/modified blob bodies)
 *   - merge/rebase conflict introspection
 *   - worktree bootstrap status (setup commands progress)
 *   - AI generation: commit message + PR description via POST /api/chats/{id}/generate
 */

import type { GitStashEntry, GitWorktreeBootstrapStatus, MergeConflictDetails } from '@/lib/api/types';
import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';
import { useSessionUIStore } from '@/sync/session-ui-store';

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

// ── Public OC-parity types ──────────────────────────────────────────

export type { GitStashEntry, MergeConflictDetails } from '@/lib/api/types';

export interface GitRemote {
	name:     string;
	fetchUrl: string;
	pushUrl:  string;
}

export interface CommitFileDiffResponse {
	original: string;
	modified: string;
	isBinary: boolean;
}

export interface GeneratedCommitMessage {
	subject:    string;
	highlights: string[];
}

export interface GeneratedPullRequestDescription {
	title: string;
	body:  string;
}

// ── Repo introspection ─────────────────────────────────────────────

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
	worktree: {
		list: async (path: string) => {
			try {
				const trimmed = path?.trim();
				if (!trimmed) return [];
				return await LisRpcClient.instance().listWorktrees(trimmed) as unknown[];
			} catch (err) {
				if (err instanceof LisRpcError) return [];
				throw err;
			}
		},
	},
};

/** Get the git API root object — OC parity for components that just probe for availability. */
export function getGitApi(): typeof git { return git; }

// ── Branch CRUD ────────────────────────────────────────────────────

export interface DeleteBranchPayload { branch: string; force?: boolean }

/**
 * Delete a branch. The backend refuses unmerged deletes unless `force: true` —
 * the dialog uses the "not fully merged" error message to prompt the user
 * for a force-delete on the next click.
 */
export async function deleteGitBranch(path: string, payload: DeleteBranchPayload): Promise<{ success: boolean }> {
	const trimmedPath   = path?.trim();
	const trimmedBranch = payload?.branch?.trim();
	if (!trimmedPath || !trimmedBranch) throw new Error('path and branch are required');
	await LisRpcClient.instance().deleteGitBranch(trimmedPath, trimmedBranch, Boolean(payload.force));
	return { success: true };
}

/** Rename a local branch. Throws on missing source / colliding target. */
export async function renameBranch(path: string, oldName: string, newName: string): Promise<{ success: boolean; branch: string }> {
	const trimmedPath = path?.trim();
	const oldTrim     = oldName?.trim();
	const newTrim     = newName?.trim();
	if (!trimmedPath || !oldTrim || !newTrim) throw new Error('path, oldName and newName are required');
	const result = await LisRpcClient.instance().renameGitBranch(trimmedPath, oldTrim, newTrim);
	return { success: true, branch: result.name };
}

// ── Stash ──────────────────────────────────────────────────────────

interface StashEntryWire {
	index:         number;
	ref:           string;
	hash:          string;
	message:       string;
	at:            string;
	relative_time: string;
}

export async function listGitStashes(path: string): Promise<{ stashes: GitStashEntry[] }> {
	const trimmed = path?.trim();
	if (!trimmed) return { stashes: [] };
	try {
		const raw = await LisRpcClient.instance().listGitStashes(trimmed);
		return { stashes: raw.stashes.map(stashEntryFromWire) };
	} catch (err) {
		if (err instanceof LisRpcError) return { stashes: [] };
		throw err;
	}
}

export async function stashGitChanges(
	path: string,
	options: { message?: string; includeUntracked?: boolean } = {},
): Promise<{ success: boolean; created: boolean; message: string; output: string }> {
	const trimmed = path?.trim();
	if (!trimmed) return { success: false, created: false, message: 'no path', output: '' };
	const includeUntracked = options.includeUntracked !== false; // default true — matches OC's defaults
	const result = await LisRpcClient.instance().saveGitStash(trimmed, options.message?.trim() || undefined, includeUntracked);
	return {
		success: result.success,
		created: result.created,
		message: result.message,
		output:  result.output,
	};
}

export async function applyGitStash(path: string, options: { ref: string }): Promise<{ success: boolean; ref: string }> {
	const trimmed = path?.trim();
	if (!trimmed || !options?.ref) throw new Error('path and ref are required');
	return LisRpcClient.instance().applyGitStash(trimmed, options.ref);
}

export async function popGitStash(path: string, options: { ref: string }): Promise<{ success: boolean; ref: string }> {
	const trimmed = path?.trim();
	if (!trimmed || !options?.ref) throw new Error('path and ref are required');
	return LisRpcClient.instance().popGitStash(trimmed, options.ref);
}

export async function dropGitStash(path: string, options: { ref: string }): Promise<{ success: boolean; ref: string }> {
	const trimmed = path?.trim();
	if (!trimmed || !options?.ref) throw new Error('path and ref are required');
	return LisRpcClient.instance().dropGitStash(trimmed, options.ref);
}

export async function countGitStashFiles(path: string, refs: string[]): Promise<{ counts: Record<string, number> }> {
	const trimmed = path?.trim();
	if (!trimmed || refs.length === 0) return { counts: {} };
	try {
		return await LisRpcClient.instance().countGitStashFiles(trimmed, refs);
	} catch (err) {
		if (err instanceof LisRpcError) return { counts: {} };
		throw err;
	}
}

function stashEntryFromWire(raw: StashEntryWire): GitStashEntry {
	return {
		ref:          raw.ref,
		message:      raw.message,
		relativeTime: raw.relative_time,
		hash:         raw.hash,
	};
}

// ── Commit / conflict diff ─────────────────────────────────────────

/**
 * Side-by-side original/modified body for one file in one commit. The
 * <c>isBinary</c> flag tells the Monaco diff viewer to render a placeholder
 * instead of trying to highlight binary bytes.
 */
export async function getCommitFileDiff(
	path: string,
	commitSha: string,
	filePath: string,
	_isBinary: boolean = false,
): Promise<CommitFileDiffResponse> {
	const trimmedPath = path?.trim();
	if (!trimmedPath) throw new Error('path is required');
	if (!commitSha)   throw new Error('commitSha is required');
	if (!filePath)    throw new Error('filePath is required');

	const raw = await LisRpcClient.instance().getCommitFileDiff(trimmedPath, commitSha, filePath);
	return { original: raw.original, modified: raw.modified, isBinary: raw.is_binary };
}

/**
 * Snapshot the current merge/rebase conflict state. Returns an "empty" payload
 * with no unmerged files when the working tree is clean (204 No Content from the
 * backend) — the dialog renders its empty state instead of crashing.
 */
export async function getConflictDetails(path: string): Promise<MergeConflictDetails> {
	const trimmed = path?.trim();
	if (!trimmed) return makeEmptyConflict();

	const raw = await LisRpcClient.instance().getGitConflictDetails(trimmed);
	if (!raw) return makeEmptyConflict();
	return {
		operation:       raw.operation === 'rebase' ? 'rebase' : 'merge',
		statusPorcelain: raw.status_porcelain,
		unmergedFiles:   raw.unmerged_files,
		diff:            raw.diff,
		headInfo:        raw.head_info ?? '',
	};
}

function makeEmptyConflict(): MergeConflictDetails {
	return {
		operation:       'merge',
		statusPorcelain: '',
		unmergedFiles:   [],
		diff:            '',
		headInfo:        '',
	};
}

// ── Worktree bootstrap ─────────────────────────────────────────────

/**
 * Read the bootstrap sidecar at the worktree root. Missing sidecar → status:
 * 'ready' so polling loops complete on the first call.
 */
export async function getGitWorktreeBootstrapStatus(worktreePath: string): Promise<GitWorktreeBootstrapStatus> {
	const trimmed = worktreePath?.trim();
	if (!trimmed) return { status: 'ready', error: null, updatedAt: 0 };
	try {
		const raw = await LisRpcClient.instance().getWorktreeBootstrapStatus(trimmed);
		const status: GitWorktreeBootstrapStatus['status'] =
			raw.status === 'pending' || raw.status === 'failed' ? raw.status : 'ready';
		const updatedAt = raw.updated_at ? new Date(raw.updated_at).getTime() : 0;
		return { status, error: raw.error ?? null, updatedAt: Number.isFinite(updatedAt) ? updatedAt : 0 };
	} catch (err) {
		if (err instanceof LisRpcError) return { status: 'ready', error: null, updatedAt: 0 };
		throw err;
	}
}

/** Mark a worktree's bootstrap progress. Used after running setup commands. */
export async function setGitWorktreeBootstrapStatus(
	worktreePath: string,
	status: 'pending' | 'ready' | 'failed',
	error?: string | null,
): Promise<void> {
	const trimmed = worktreePath?.trim();
	if (!trimmed) throw new Error('worktreePath is required');
	await LisRpcClient.instance().setWorktreeBootstrapStatus(trimmed, status, error ?? null);
}

// ── AI generation: commit message + PR description ─────────────────

/**
 * Generate a commit subject + highlights from the current diff using the
 * active chat's agent. We send a visible prompt (what the user sees in the
 * chat if they look) plus a hidden prompt with the JSON schema instructions,
 * and parse the JSON out of the assistant text.
 *
 * Throws when no chat is active — the dialog surfaces this as a toast asking
 * the user to start a chat first.
 */
export async function generateCommitMessage(
	_path: string,
	files: string[],
	options?: { model?: string },
): Promise<{ message: GeneratedCommitMessage }> {
	const chatId = useSessionUIStore.getState().currentSessionId;
	if (!chatId) throw new Error('Select existing chat for generation');

	const visible = `Generate a commit message for the staged changes.\n\nFiles being committed:\n${files.map((f) => `- ${f}`).join('\n')}`;
	const hidden  = [
		'Return ONLY a JSON object on the very last line of your response.',
		'Shape: { "subject": string, "highlights": string[] }',
		'  - "subject" is a concise gitmoji-prefixed conventional-commit subject line, max 72 chars.',
		'  - "highlights" is an optional array of 0-3 short bullets summarising the change.',
		'No prose outside the JSON. No code fences around the JSON.',
	].join('\n');

	const result = await LisRpcClient.instance().generateChat(chatId, {
		visible_prompt: visible,
		hidden_prompt:  hidden,
		kind:           'commit',
		model:          options?.model,
	});

	const parsed = extractJsonObject(result.text);
	const subject    = typeof parsed?.subject    === 'string' ? parsed.subject.trim() : '';
	const highlights = Array.isArray(parsed?.highlights)
		? (parsed.highlights as unknown[]).filter((x): x is string => typeof x === 'string').slice(0, 3)
		: [];
	if (!subject) throw new Error('Generated commit message had no subject');
	return { message: { subject, highlights } };
}

/**
 * Generate a PR title + body for the head→base range. Same prompt+JSON pattern
 * as <c>generateCommitMessage</c>; payload provides the branch context.
 */
export async function generatePullRequestDescription(
	_path: string,
	payload: { base: string; head: string; context?: string; model?: string },
): Promise<GeneratedPullRequestDescription> {
	const chatId = useSessionUIStore.getState().currentSessionId;
	if (!chatId) throw new Error('Select existing chat for generation');

	const ctx = payload.context?.trim() ? `\n\nAdditional context:\n${payload.context.trim()}` : '';
	const visible = `Generate a pull request title + body for merging ${payload.head} into ${payload.base}.${ctx}`;
	const hidden  = [
		'Return ONLY a JSON object on the very last line of your response.',
		'Shape: { "title": string, "body": string }',
		'  - "title" is a short PR title (max 80 chars).',
		'  - "body" is a markdown summary covering the change set, motivation, and a brief test plan.',
		'No prose outside the JSON. No code fences around the JSON.',
	].join('\n');

	const result = await LisRpcClient.instance().generateChat(chatId, {
		visible_prompt: visible,
		hidden_prompt:  hidden,
		kind:           'pr',
		model:          payload.model,
	});

	const parsed = extractJsonObject(result.text);
	const title = typeof parsed?.title === 'string' ? parsed.title.trim() : '';
	const body  = typeof parsed?.body  === 'string' ? parsed.body.trim()  : '';
	if (!title || !body) throw new Error('Generated PR description was missing title or body');
	return { title, body };
}

/**
 * Robust JSON extractor — models sometimes wrap structured output in prose or
 * code fences, so we scan the trailing braces for the largest parseable object.
 */
function extractJsonObject(text: string): Record<string, unknown> | null {
	const cleaned = text.trim();
	const fenced  = /```(?:json)?\s*([\s\S]*?)```/i.exec(cleaned);
	const candidate = (fenced?.[1] ?? cleaned).trim();
	const start = candidate.indexOf('{');
	if (start < 0) return null;
	for (let end = candidate.length; end > start; end -= 1) {
		if (candidate[end - 1] !== '}') continue;
		try {
			const parsed: unknown = JSON.parse(candidate.slice(start, end));
			if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
				return parsed as Record<string, unknown>;
			}
		} catch {
			// keep scanning
		}
	}
	return null;
}
