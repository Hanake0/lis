import { create } from 'zustand';

import type { Agent, PermissionConfig } from '@opencode-ai/sdk/v2';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native agents store. Backed by `GET /api/agents` (Lis's read-side
 * AgentsController) and exposes the OC `useAgentsStore` surface so
 * byte-identical sidebar dialogs + AgentSelector + AgentMentionAutocomplete
 * compile unchanged.
 *
 * **Read surface (fully wired)**:
 * - `agents`, `isLoading`, `selectedAgentName`
 * - `loadAgents()` — POSTs nothing; calls `LisRpcClient.listAgents()` and
 *   normalises each row to `AgentWithExtras` so `isAgentBuiltIn` resolves.
 * - `getAgentByName(name)`, `getVisibleAgents()`
 * - `setSelectedAgent(name)`
 *
 * **Write surface (deferred to Stage 13)**:
 * - `createAgent`, `updateAgent`, `deleteAgent` throw a clear error so the
 *   AgentsPage UI surfaces "not yet supported" instead of silently failing.
 *   The AgentEntity schema is missing OC's `temperature`/`top_p`/`mode`/
 *   `permission` fields — wiring those would force a Lis-flavoured port of
 *   the agent settings UI, which the plan defers to Stage 13.
 * - `agentDraft` / `setAgentDraft` are in-memory only — useful for the New
 *   Agent dialog flow to round-trip a draft between sidebar + page.
 *
 * **Lis ↔ OC mapping**:
 * - `is_default = true` (Lis) → `native = true` (OC). `isAgentBuiltIn`
 *   honours both `native` + `builtIn`, so our flag flows through cleanly.
 * - No file-system `scope` (user/project) — Lis agents are global DB rows.
 *   Consumers that read `agent.scope` get `undefined`, which renders fine
 *   in OC's AgentsSidebar (the badge just doesn't show).
 */

export type AgentScope = 'user' | 'project';

export interface AgentConfig {
	name:        string;
	description?: string;
	model?:      string | null;
	temperature?: number;
	top_p?:      number;
	prompt?:     string;
	mode?:       'primary' | 'subagent' | 'all';
	permission?: PermissionConfig | null;
	disable?:    boolean;
	scope?:      AgentScope;
}

export interface AgentDraft {
	name:        string;
	scope:       AgentScope;
	description?: string;
	model?:      string | null;
	temperature?: number;
	top_p?:      number;
	prompt?:     string;
	mode?:       'primary' | 'subagent' | 'all';
	permission?: PermissionConfig;
	disable?:    boolean;
}

/** Extended Agent type for API properties that the SDK's Agent type doesn't cover. */
export type AgentWithExtras = Agent & {
	native?:       boolean;
	hidden?:       boolean;
	options?:      { hidden?: boolean };
	scope?:        AgentScope;
	group?:        string;
	is_default?:   boolean;
	display_name?: string;
};

/** Lis agent DTO shape (snake_case from the wire). Kept loose so future fields don't break. */
interface LisAgentDto {
	name:             string;
	display_name?:    string | null;
	model:            string;
	thinking_effort?: string | null;
	tool_profile?:    string | null;
	is_default?:      boolean;
	native?:          boolean;
	max_tokens?:      number;
	context_budget?:  number;
	description?:     string | null;
}

interface AgentsStore {
	selectedAgentName: string | null;
	agents:            AgentWithExtras[];
	isLoading:         boolean;
	agentDraft:        AgentDraft | null;
	lastError:         string | null;

	setSelectedAgent: (name: string | null) => void;
	setAgentDraft:    (draft: AgentDraft | null) => void;
	loadAgents:       () => Promise<boolean>;
	createAgent:      (config: AgentConfig) => Promise<boolean>;
	updateAgent:      (name: string, config: Partial<AgentConfig>) => Promise<boolean>;
	deleteAgent:      (name: string) => Promise<boolean>;
	getAgentByName:   (name: string) => AgentWithExtras | undefined;
	getVisibleAgents: () => AgentWithExtras[];
}

// ── Pure helpers exported alongside the store (OC parity) ──────────

/** True for built-in (Lis: `is_default`) or for OC-shaped agents flagged via `native`/`builtIn`. */
export const isAgentBuiltIn = (agent: Agent): boolean => {
	const x = agent as AgentWithExtras & { builtIn?: boolean };
	return x.native === true || x.builtIn === true || x.is_default === true;
};

/** True for internal agents that OC hides (title / compaction / summary helpers). */
export const isAgentHidden = (agent: Agent): boolean => {
	const x = agent as AgentWithExtras;
	return x.hidden === true || x.options?.hidden === true;
};

export const filterVisibleAgents = (agents: Agent[]): Agent[] => agents.filter(a => !isAgentHidden(a));

// ── DTO → AgentWithExtras ──────────────────────────────────────────

function fromLisDto(dto: LisAgentDto): AgentWithExtras {
	// The SDK Agent type is loose — we attach Lis-specific fields directly. Consumers
	// reading `agent.model` / `agent.name` work regardless of provider.
	const base = {
		name:         dto.name,
		model:        dto.model,
		display_name: dto.display_name ?? undefined,
	} as Agent & Record<string, unknown>;

	return {
		...base,
		native:       dto.native ?? dto.is_default ?? false,
		is_default:   dto.is_default ?? false,
		display_name: dto.display_name ?? undefined,
		description:  dto.description ?? undefined,
	} as AgentWithExtras;
}

// ── Throw helper for the deferred CRUD surface ─────────────────────

function notSupportedYet(action: string): never {
	throw new Error(
		`[useAgentsStore] ${action} is not wired in Lis yet — see Stage 13 of the OC sidebar parity plan. `
		+ `The current AgentEntity is missing OC's temperature/top_p/mode/permission columns; `
		+ `a follow-up will introduce the Lis-flavoured agent-settings shape + REST surface.`
	);
}

// ── Store ──────────────────────────────────────────────────────────

export const useAgentsStore = create<AgentsStore>((set, get) => ({
	selectedAgentName: null,
	agents:            [],
	isLoading:         false,
	agentDraft:        null,
	lastError:         null,

	setSelectedAgent: (name) => set({ selectedAgentName: name }),
	setAgentDraft:    (draft) => set({ agentDraft: draft }),

	loadAgents: async () => {
		if (get().isLoading) return true; // coalesce concurrent loads
		set({ isLoading: true, lastError: null });
		try {
			const rows = await LisRpcClient.instance().listAgents() as LisAgentDto[];
			const mapped = rows.map(fromLisDto);
			set({ agents: mapped, isLoading: false });
			return true;
		} catch (err) {
			set({ isLoading: false, lastError: errorMessage(err) });
			return false;
		}
	},

	createAgent: async (_config) => { notSupportedYet('createAgent'); },
	updateAgent: async (_name, _config) => { notSupportedYet('updateAgent'); },
	deleteAgent: async (_name) => { notSupportedYet('deleteAgent'); },

	getAgentByName:   (name) => get().agents.find(a => a.name === name),
	getVisibleAgents: () => get().agents.filter(a => !isAgentHidden(a)),
}));

/** Side-effect needed by the OC pattern: refresh after an OpenCode restart. In Lis this is a noop. */
export function reloadOpenCodeConfiguration(): void { /* not applicable to Lis */ }

function errorMessage(err: unknown): string {
	if (err instanceof LisRpcError) return err.message;
	if (err instanceof Error)        return err.message;
	return String(err);
}
