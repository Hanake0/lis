import { create } from 'zustand';

export interface ModelMetadata {
	id: string;
	name?: string;
	limit?: { context?: number; output?: number };
}

export interface ProviderEntry {
	id: string;
	name: string;
	models: ModelMetadata[];
}

export interface AgentEntry {
	id: string;
	name: string;
	model?: string;
	provider?: string;
}

interface ConfigStore {
	isInitialized: boolean;
	isConnected: boolean;
	providers: ProviderEntry[];
	agents: AgentEntry[];
	getCurrentModel: () => ModelMetadata | undefined;
	loadProviders: () => Promise<void>;
	loadAgents: () => Promise<void>;
	initializeApp: () => Promise<void>;
}

export const useConfigStore = create<ConfigStore>((set, _get) => ({
	isInitialized: false,
	isConnected: false,
	providers: [],
	agents: [],
	getCurrentModel: () => undefined,
	loadProviders: async () => {},
	loadAgents: async () => {},
	initializeApp: async () => {
		set({ isInitialized: true, isConnected: true });
	},
}));
