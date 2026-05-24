import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';
import type { CreateMultiRunParams, CreateMultiRunResult } from '@/types/multirun';

/**
 * Multi-run launcher store. POSTs to `/api/multi-run` (Stage 7 backend) with a
 * fan-out payload. Surface mirrors OC's `useMultiRunStore` so AgentManagerView
 * keeps compiling.
 *
 * Difference from OC desktop: the OC version creates worktrees + sessions per
 * model as part of the run. The Lis backend instead targets *existing* chats
 * (one per model variant). The dialog UI that drives this store will populate
 * `groups[].models[].variant` with the chat id to target — for installs where
 * the dialog hasn't been wired through yet, we surface a clear validation
 * error rather than silently no-op.
 *
 * If worktree+session orchestration is needed end-to-end (the way OC desktop
 * does it), that's a follow-up that ports `createWorktreeWithDefaults` and
 * `opencodeClient.createSession` to Lis equivalents. Today we cover the
 * already-existing-chats path.
 */

interface MultiRunStore {
	isLoading: boolean;
	error:     string | null;

	createMultiRun: (params: CreateMultiRunParams) => Promise<CreateMultiRunResult | null>;
	clearError:     () => void;
}

interface MultiRunTarget {
	chat_id:      string;
	agent_name?:  string;
	model?:       string;
}

function toGitSafeSlug(value: string): string {
	return value
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '')
		.substring(0, 50);
}

/**
 * Resolve a target chat id from a model selection. Today the dialog passes
 * the existing chat id through `variant`; if not provided, we cannot target.
 */
function resolveTargetChatId(model: { variant?: string }): string | null {
	const candidate = model.variant?.trim();
	return candidate ? candidate : null;
}

export const useMultiRunStore = create<MultiRunStore>((set) => ({
	isLoading: false,
	error:     null,

	clearError: () => set({ error: null }),

	createMultiRun: async (params) => {
		const groupName = params.name.trim();
		const { groups, agent } = params;

		if (!groupName)                       { set({ error: 'Group name is required' });           return null; }
		if (!groups || groups.length === 0)   { set({ error: 'At least one run group is required' }); return null; }
		for (let gi = 0; gi < groups.length; gi++) {
			if (!groups[gi].prompt.trim())    { set({ error: `Group ${gi + 1}: prompt is required` }); return null; }
			if (groups[gi].models.length < 1) { set({ error: `Group ${gi + 1}: select at least 1 model` }); return null; }
			if (groups[gi].models.length > 5) { set({ error: `Group ${gi + 1}: maximum 5 models allowed` }); return null; }
		}

		set({ isLoading: true, error: null });
		try {
			const groupSlug = toGitSafeSlug(groupName);
			const sessionIds: string[] = [];

			// Lis backend takes one `{ prompt, targets[] }` per call. We loop per
			// group so per-group prompts and model fan-out stay accurate.
			for (const group of groups) {
				const targets: MultiRunTarget[] = [];
				for (const model of group.models) {
					const chatId = resolveTargetChatId(model);
					if (!chatId) continue;
					targets.push({
						chat_id:     chatId,
						agent_name:  agent,
						model:       `${model.providerID}/${model.modelID}`,
					});
				}
				if (targets.length === 0) continue;

				await LisRpcClient.instance().runMultiRun({
					prompt:  group.prompt,
					targets,
				});
				for (const t of targets) sessionIds.push(t.chat_id);
			}

			if (sessionIds.length === 0) {
				set({ error: 'No targetable chats resolved (pass a chat id as `variant` on each model)', isLoading: false });
				return null;
			}

			set({ isLoading: false });
			return { groupSlug, sessionIds, firstSessionId: sessionIds[0] };
		} catch (err) {
			const message = err instanceof LisRpcError ? err.message
				:           err instanceof Error        ? err.message
				:           String(err);
			set({ error: message, isLoading: false });
			return null;
		}
	},
}));
