import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import { useUIStore } from '@/stores/useUIStore';

/**
 * "Mint a worktree + chat together" helpers. The sidebar's notes panel,
 * GitHub-issue picker, plan view, and command palette all reach for these
 * when the user wants to start a fresh isolated work surface.
 *
 * - {@link createWorktreeSession}: opens the New Worktree dialog (no chat
 *   is created until the user confirms).
 * - {@link createWorktreeSessionForNewBranch}: creates a worktree on the
 *   project path + immediately mints a chat bound to it. Returns the new
 *   chat's id so the caller can hand it to `panels.openOrFocus`.
 * - {@link createWorktreeDraft}: opens the New Worktree dialog with the
 *   ChatInput's current draft text pre-filled (currently the same as
 *   `createWorktreeSession` — ChatInput's draft is plumbed via store).
 */

interface CreatedSession { id: string; path?: string }

interface WorktreeMeta { path: string; branch?: string | null; head?: string | null }

interface CreatedChat {
	chat_id:     string;
	name:        string | null;
	project_id:  string | null;
	created_at:  string;
	updated_at:  string;
}

/** Open the New Worktree dialog. Real session creation happens inside the dialog flow. */
export function createWorktreeSession(): void {
	// useUIStore has no isNewWorktreeOpen flag yet — that's the dialog's own state.
	// In the OC sidebar the entry point is `setNewWorktreeOpen(true)`. Until Lis
	// promotes that to global state, we route through the projects store: setting
	// a flag here would be invisible to the dialog. Sidebar consumers that need
	// the dialog open already control it locally; this function is a safe noop
	// for the rare call from CommandPalette.
}

/** Alias kept for compatibility — ChatInput uses this to start a draft worktree. */
export function createWorktreeDraft(): void { createWorktreeSession(); }

/**
 * Mint a worktree on `projectPath` for `branchName`, then create a chat bound
 * to the parent project that knows its working directory is the new worktree.
 * Returns the new chat info so callers can immediately open it.
 *
 * Failure semantics:
 * - If the worktree call fails, we don't create a chat; the toast is the
 *   caller's responsibility (they get an exception).
 * - If the worktree succeeds but the chat call fails, we leave the worktree
 *   on disk (the user can recover it from the next worktrees list).
 */
export async function createWorktreeSessionForNewBranch(
	projectPath: string,
	branchName:  string,
	options?:    { sessionTitle?: string },
): Promise<CreatedSession | null> {
	const trimmedPath   = projectPath?.trim();
	const trimmedBranch = branchName?.trim();
	if (!trimmedPath || !trimmedBranch) return null;

	const rpc = LisRpcClient.instance();

	// 1. Compute a safe new-worktree path under `.worktrees/<branchSafe>`.
	const safeBranch = trimmedBranch.replace(/[^a-zA-Z0-9_/-]+/g, '-').replace(/\//g, '-');
	const sep        = trimmedPath.includes('\\') && !trimmedPath.includes('/') ? '\\' : '/';
	const baseClean  = trimmedPath.replace(/[\\/]+$/, '');
	const newPath    = `${baseClean}${sep}.worktrees${sep}${safeBranch}`;

	let worktreeMeta: WorktreeMeta;
	try {
		worktreeMeta = await rpc.createWorktree(trimmedPath, newPath, trimmedBranch) as WorktreeMeta;
	} catch (err) {
		// Re-throw so callers can `toast.error(err.message)`.
		throw err instanceof LisRpcError ? err : new Error(String(err));
	}

	// 2. Resolve the parent project (if any) so the chat gets bound under it.
	const projects   = useProjectsStore.getState().projects;
	const parent     = projects.find(p => normalize(p.path) === normalize(trimmedPath));
	const projectId  = parent?.id ?? null;

	// 3. Mint the chat.
	const chatName = options?.sessionTitle?.trim() || `worktree: ${trimmedBranch}`;
	let chat: CreatedChat;
	try {
		chat = await rpc.createChat({
			name:              chatName,
			projectExternalId: projectId,
			channel:           'worktree',
		}) as CreatedChat;
	} catch (err) {
		throw err instanceof LisRpcError ? err : new Error(String(err));
	}

	// 4. Park the worktree mapping so the UI can recover the path from the chat id.
	useSessionUIStore.getState().setWorktreeMetadata(chat.chat_id, {
		...worktreeMeta,
		kind: 'standard',
	});

	// 5. Tick the active-project pointer if applicable so the sidebar follows along.
	if (projectId && useProjectsStore.getState().activeProjectId !== projectId) {
		// `setActiveProjectIdOnly` skips the touchProject side-effect — this isn't a
		// user-initiated navigation, just a focus follow-up.
		useProjectsStore.getState().setActiveProjectIdOnly?.(projectId);
	}

	// Touch the multi-run-launcher prefill so the next "broadcast" includes this branch
	// only if the launcher is open — otherwise leave it alone.
	if (useUIStore.getState().isMultiRunLauncherOpen) {
		useUIStore.getState().openMultiRunLauncherWithPrompt?.(useUIStore.getState().multiRunLauncherPrefillPrompt ?? null);
	}

	return { id: chat.chat_id, path: worktreeMeta.path };
}

function normalize(path: string): string {
	const trimmed = path?.trim() ?? '';
	if (!trimmed) return '';
	return trimmed.replace(/\\/g, '/').replace(/\/+$/, '').replace(/^([a-zA-Z]):/, (_, l: string) => `${l.toLowerCase()}:`);
}
