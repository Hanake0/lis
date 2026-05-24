import { create } from 'zustand';

import type { Agent, PermissionConfig } from '@opencode-ai/sdk/v2';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native agents store. Backed by the full CRUD surface on
 * `/api/agents` and exposes the OC `useAgentsStore` shape so byte-identical
 * sidebar dialogs + AgentSelector + AgentMentionAutocomplete compile
 * unchanged.
 *
 * Surface (all wired):
 * - `agents`, `isLoading`, `selectedAgentName`, `agentDraft`
 * - `loadAgents()` calls `LisRpcClient.listAgents()` and normalises each row
 * - `createAgent(config)` → POST /api/agents
 * - `updateAgent(name, config)` → PATCH /api/agents/{name}
 * - `deleteAgent(name)` → DELETE /api/agents/{name} (rejects built-ins with 409)
 * - `getAgentByName`, `getVisibleAgents`
 *
 * Lis ↔ OC mapping:
 * - `is_default = true` (Lis) → `native = true` (OC). `isAgentBuiltIn`
 *   honours both `native` + `builtIn`, so our flag flows through cleanly.
 * - `permission` (OC's structured tree) round-trips through `permission_json`
 *   as a JSON string on the wire. The store JSON.stringifies on write and
 *   JSON.parses on read so consumers see the structured tree.
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

/** Lis agent DTO shape (snake_case from the wire). */
interface LisAgentDto {
	name:             string;
	display_name?:    string | null;
	description?:     string | null;
	model:            string;
	thinking_effort?: string | null;
	tool_profile?:    string | null;
	is_default?:      boolean;
	native?:          boolean;
	max_tokens?:      number;
	context_budget?:  number;
	mode?:            string | null;
	prompt?:          string | null;
	temperature?:     number | null;
	top_p?:           number | null;
	permission_json?: string | null;
	scope?:           string | null;
	disabled?:        boolean;
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

// ── DTO ↔ store conversions ───────────────────────────────────────

function parsePermission(raw: string | null | undefined): PermissionConfig | null {
	if (!raw) return null;
	try { return JSON.parse(raw) as PermissionConfig; }
	catch { return null; }
}

function fromLisDto(dto: LisAgentDto): AgentWithExtras {
	// The SDK Agent type is loose — we attach Lis-specific fields directly. Consumers
	// reading `agent.model` / `agent.name` work regardless of provider.
	const base = {
		name:         dto.name,
		model:        dto.model,
		display_name: dto.display_name ?? undefined,
	} as Agent & Record<string, unknown>;

	const scope: AgentScope | undefined =
		dto.scope === 'user' || dto.scope === 'project' ? dto.scope : undefined;

	return {
		...base,
		native:       dto.native ?? dto.is_default ?? false,
		is_default:   dto.is_default ?? false,
		display_name: dto.display_name ?? undefined,
		description:  dto.description ?? undefined,
		scope,
		hidden:       dto.disabled ?? false,
		temperature:  dto.temperature ?? undefined,
		top_p:        dto.top_p ?? undefined,
		mode:         dto.mode ?? undefined,
		prompt:       dto.prompt ?? undefined,
		permission:   parsePermission(dto.permission_json),
	} as AgentWithExtras;
}

function toLisPayload(config: Partial<AgentConfig & AgentDraft>): Record<string, unknown> {
	const payload: Record<string, unknown> = {};
	if (config.name        !== undefined) payload.name            = config.name;
	if (config.description !== undefined) payload.description     = config.description;
	if (config.model       !== undefined) payload.model           = config.model;
	if (config.prompt      !== undefined) payload.prompt          = config.prompt;
	if (config.mode        !== undefined) payload.mode            = config.mode;
	if (config.temperature !== undefined) payload.temperature     = config.temperature;
	if (config.top_p       !== undefined) payload.top_p           = config.top_p;
	if (config.scope       !== undefined) payload.scope           = config.scope;
	if (config.disable     !== undefined) payload.disabled        = config.disable;
	if (config.permission  !== undefined) {
		payload.permission_json = config.permission === null ? null : JSON.stringify(config.permission);
	}
	return payload;
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

	createAgent: async (config) => {
		try {
			const dto = await LisRpcClient.instance().createAgent(toLisPayload(config)) as LisAgentDto;
			const mapped = fromLisDto(dto);
			set((s) => ({ agents: [...s.agents.filter(a => a.name !== mapped.name), mapped], lastError: null }));
			return true;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return false;
		}
	},

	updateAgent: async (name, config) => {
		try {
			const dto = await LisRpcClient.instance().updateAgent(name, toLisPayload(config)) as LisAgentDto;
			const mapped = fromLisDto(dto);
			set((s) => ({ agents: s.agents.map(a => a.name === name ? mapped : a), lastError: null }));
			return true;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return false;
		}
	},

	deleteAgent: async (name) => {
		try {
			await LisRpcClient.instance().deleteAgent(name);
			set((s) => ({ agents: s.agents.filter(a => a.name !== name), lastError: null }));
			return true;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return false;
		}
	},

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
