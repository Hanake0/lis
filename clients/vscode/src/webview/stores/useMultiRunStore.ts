import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { useSessionUIStore } from '@/sync/session-ui-store';
import type { CreateMultiRunParams, CreateMultiRunResult } from '@/types/multirun';

/**
 * Multi-run launcher store. Orchestrates the *full* flow: per model, optionally
 * create a worktree, mint a chat, then POST `/api/multi-run` per group with the
 * resolved targets so the backend coordinator can dispatch the prompt.
 *
 * Mirrors OC's `useMultiRunStore` API + behaviour as closely as we can given
 * the Lis transport differences (REST + SSE, no `opencodeClient.withDirectory`):
 *
 * - Validates the same way (name, ≥1 group, prompt per group, 1–5 models per group).
 * - Resolves the active project from `useProjectsStore`; treats no-project as
 *   "ungrouped, no isolation".
 * - When the project path is a git repo (probed via `getRepoStatus`) and
 *   `isolateRuns !== false`, a fresh worktree is spun up per model so the
 *   parallel runs can't trample each other.
 * - For repeats of the same `provider:model` inside a group, suffixes the
 *   worktree path with `/n` (same shape as OC) and tags the chat name with `#n`.
 * - Per-model worktree+chat failures are swallowed and logged — they don't take
 *   down the whole batch. If *no* chats are created at all, the store reports
 *   "Failed to create any sessions".
 * - One POST to `/api/multi-run` per group (the backend coordinator handles the
 *   fan-out per target inside the group). Errors here surface as `error` on the
 *   store. The created chats survive so the user can still drive them by hand.
 */

interface MultiRunStore {
	isLoading: boolean;
	error:     string | null;

	createMultiRun: (params: CreateMultiRunParams) => Promise<CreateMultiRunResult | null>;
	clearError:     () => void;
}

interface CreatedRun {
	chatId:       string;
	worktreePath: string | null;
	providerID:   string;
	modelID:      string;
	prompt:       string;
	agentName?:   string;
	groupIndex:   number;
}

interface RepoStatus { isGitRepo?: boolean }

interface WorktreeMetadata {
	path:    string;
	branch?: string | null;
	head?:   string | null;
}

function toGitSafeSlug(value: string): string {
	return value
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '')
		.substring(0, 50);
}

function toModelSlug(providerID: string, modelID: string): string {
	return `${toGitSafeSlug(providerID)}-${toGitSafeSlug(modelID)}`.substring(0, 60);
}

/**
 * Join base directory + relative segments using whatever separator base already uses.
 * Backslashes win iff base has any (Windows-style); otherwise we forward-slash. Lets
 * us synthesise reasonable worktree paths without pulling in `path-browserify`.
 */
function joinPath(base: string, ...segments: string[]): string {
	const sep      = base.includes('\\') && !base.includes('/') ? '\\' : '/';
	const trimmed  = base.replace(/[\\/]+$/, '');
	const cleaned  = segments.map(s => s.replace(/[\\/]+/g, sep).replace(/^[\\/]+|[\\/]+$/g, ''));
	return [trimmed, ...cleaned].join(sep);
}

/** Compute the worktree-name suffix, honouring per-group prefix (`g{n}/`) when there are multiple groups. */
function computeWorktreeName(opts: {
	groupSlug:     string;
	modelSlug:     string;
	modelIndex?:   number;
	groupCount:    number;
	groupIndex:    number;
}): string {
	const modelPart = opts.modelIndex !== undefined
		? `${opts.groupSlug}/${opts.modelSlug}/${opts.modelIndex}`
		: `${opts.groupSlug}/${opts.modelSlug}`;
	return opts.groupCount > 1 ? `g${opts.groupIndex + 1}/${modelPart}` : modelPart;
}

function buildChatTitle(opts: { groupSlug: string; providerID: string; modelID: string; index?: number }): string {
	const tail = opts.index !== undefined ? ` #${opts.index}` : '';
	return `${opts.groupSlug}: ${opts.providerID}/${opts.modelID}${tail}`;
}

export const useMultiRunStore = create<MultiRunStore>((set) => ({
	isLoading: false,
	error:     null,

	clearError: () => set({ error: null }),

	createMultiRun: async (params) => {
		const groupName = params.name.trim();
		const { groups, agent } = params;

		// ── Validation (same rules as OC) ────────────────────────────
		if (!groupName)                          { set({ error: 'Group name is required' });            return null; }
		if (!groups || groups.length === 0)      { set({ error: 'At least one run group is required' }); return null; }
		for (let gi = 0; gi < groups.length; gi++) {
			if (!groups[gi].prompt.trim())       { set({ error: `Group ${gi + 1}: prompt is required` }); return null; }
			if (groups[gi].models.length < 1)    { set({ error: `Group ${gi + 1}: select at least 1 model` }); return null; }
			if (groups[gi].models.length > 5)    { set({ error: `Group ${gi + 1}: maximum 5 models allowed` }); return null; }
		}

		set({ isLoading: true, error: null });
		try {
			const rpc        = LisRpcClient.instance();
			const project    = useProjectsStore.getState().getActiveProject();
			const projectId  = project?.id ?? null;
			const basePath   = project?.path ?? null;

			// Probe git only if we have a path *and* the caller didn't opt out of isolation.
			let shouldIsolate = false;
			if (basePath && params.isolateRuns !== false) {
				try {
					const status = await rpc.getRepoStatus(basePath) as RepoStatus | null;
					shouldIsolate = status?.isGitRepo === true;
				} catch {
					shouldIsolate = false; // probe failure → just don't isolate
				}
			}

			const groupSlug    = toGitSafeSlug(groupName);
			const createdRuns: CreatedRun[] = [];

			for (let gi = 0; gi < groups.length; gi++) {
				const group       = groups[gi];
				const modelCounts = new Map<string, number>();
				for (const m of group.models) {
					const key = `${m.providerID}:${m.modelID}`;
					modelCounts.set(key, (modelCounts.get(key) ?? 0) + 1);
				}
				const modelIndexes = new Map<string, number>();

				for (const model of group.models) {
					const key   = `${model.providerID}:${model.modelID}`;
					const count = modelCounts.get(key) ?? 1;
					const idx   = (modelIndexes.get(key) ?? 0) + 1;
					modelIndexes.set(key, idx);

					const modelSlug    = toModelSlug(model.providerID, model.modelID);
					const worktreeName = computeWorktreeName({
						groupSlug, modelSlug,
						modelIndex: count > 1 ? idx : undefined,
						groupCount: groups.length,
						groupIndex: gi,
					});
					const chatTitle    = buildChatTitle({
						groupSlug, providerID: model.providerID, modelID: model.modelID,
						index: count > 1 ? idx : undefined,
					});

					try {
						// 1. Worktree (when isolating). Failures here skip this model only.
						let worktreePath: string | null = null;
						let worktreeMeta: WorktreeMetadata | null = null;
						if (shouldIsolate && basePath) {
							const safeBranch = worktreeName.replace(/\//g, '-');
							const newPath    = joinPath(basePath, '.worktrees', safeBranch);
							worktreeMeta     = await rpc.createWorktree(basePath, newPath, worktreeName) as WorktreeMetadata;
							worktreePath     = worktreeMeta?.path ?? newPath;
						}

						// 2. Chat. The server is idempotent on `chat_id`, but we let it mint one.
						const chat = await rpc.createChat({
							name:              chatTitle,
							projectExternalId: projectId,
							channel:           'multi-run',
						});

						// 3. Park the worktree mapping so the chat UI can surface it.
						if (worktreeMeta) {
							useSessionUIStore.getState().setWorktreeMetadata(chat.chat_id, {
								...worktreeMeta,
								kind: 'standard',
							});
						}

						createdRuns.push({
							chatId:       chat.chat_id,
							worktreePath,
							providerID:   model.providerID,
							modelID:      model.modelID,
							prompt:       group.prompt,
							agentName:    agent,
							groupIndex:   gi,
						});
					} catch (err) {
						console.warn('[MultiRun] Failed to set up run for', `${model.providerID}/${model.modelID}`, err);
					}
				}
			}

			if (createdRuns.length === 0) {
				set({ error: 'Failed to create any sessions', isLoading: false });
				return null;
			}

			// 4. One `/api/multi-run` per group (groups can have different prompts).
			//    The backend coordinator handles fan-out across the targets array.
			const runsByGroup = new Map<number, CreatedRun[]>();
			for (const run of createdRuns) {
				const arr = runsByGroup.get(run.groupIndex) ?? [];
				arr.push(run);
				runsByGroup.set(run.groupIndex, arr);
			}

			for (const [gi, runs] of runsByGroup) {
				await rpc.runMultiRun({
					prompt:  groups[gi].prompt,
					targets: runs.map(r => ({
						chat_id:    r.chatId,
						agent_name: r.agentName,
						model:      `${r.providerID}/${r.modelID}`,
					})),
				});
			}

			set({ isLoading: false });
			return {
				groupSlug,
				sessionIds:     createdRuns.map(r => r.chatId),
				firstSessionId: createdRuns[0].chatId,
			};
		} catch (err) {
			const message = err instanceof LisRpcError ? err.message
				:           err instanceof Error        ? err.message
				:           String(err);
			set({ error: message, isLoading: false });
			return null;
		}
	},
}));
