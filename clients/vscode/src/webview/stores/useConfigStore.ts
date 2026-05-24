import { create } from 'zustand';
import { useChatStore } from '@/store';

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
	getCurrentModel: () => {
		const model = useChatStore.getState().composerModel;
		return model ? { id: model, name: model } : undefined;
	},
	loadProviders: async () => {},
	loadAgents: async () => {},
	initializeApp: async () => {
		set({ isInitialized: true, isConnected: true });
	},
}));

useChatStore.subscribe((state) => {
	const connected = state.connection === 'connected';
	const agents = state.agents.map((a) => ({
		id: a.name,
		name: a.display_name ?? a.name,
		model: a.model,
	}));
	useConfigStore.setState({
		isConnected: connected,
		isInitialized: connected,
		agents,
		providers: state.composerModel ? [{
			id: 'default',
			name: 'Provider',
			models: [{ id: state.composerModel, name: state.composerModel }],
		}] : [],
	});
});
