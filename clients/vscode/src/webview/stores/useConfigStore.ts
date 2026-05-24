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

const noop: any = () => {};
const asyncNoop: any = async () => {};

export const useConfigStore = create<any>((set: any) => ({
	isInitialized: false,
	isConnected: false,
	providers: [] as ProviderEntry[],
	agents: [] as AgentEntry[],
	modelsMetadata: [] as any[],
	currentAgentName: '',
	currentModelId: '',
	currentProviderId: '',
	currentVariant: '',
	selectedProviderId: '',
	settingsDefaultAgent: '',
	settingsDefaultVariant: '',
	settingsDefaultFileViewerPreview: false,
	settingsGitmojiEnabled: false,
	settingsMessageStreamTransport: 'auto',
	voiceModeEnabled: false,
	voiceProvider: 'browser',
	browserVoice: '',
	sayVoice: '',
	openaiVoice: '',
	openaiApiKey: '',
	openaiCompatibleUrl: '',
	openaiCompatibleApiKey: '',
	openaiCompatibleVoice: '',
	openaiCompatibleTtsModel: '',
	sttProvider: 'browser',
	sttServerUrl: '',
	sttModel: '',
	sttLanguage: '',
	sttApiKey: '',
	sttSilenceThresholdDb: -40,
	sttSilenceHoldMs: 1500,
	sttTranscribeOnStop: false,
	wasmSttModel: '',
	speechRate: 1,
	speechPitch: 1,
	speechVolume: 1,
	showMessageTTSButtons: false,
	getCurrentModel: () => {
		const model = useChatStore.getState().composerModel;
		return model ? { id: model, name: model } : undefined;
	},
	getCurrentAgent: () => {
		const agent = useChatStore.getState().currentAgent;
		return agent ? { id: agent.name, name: agent.display_name ?? agent.name } : undefined;
	},
	getCurrentProvider: () => {
		const state = useConfigStore.getState();
		return state.providers.find((p: ProviderEntry) => p.id === state.currentProviderId) ?? null;
	},
	getCurrentModelVariants: () => [],
	getModelMetadata: () => null,
	getVisibleAgents: () => useChatStore.getState().agents.map((a) => ({ id: a.name, name: a.display_name ?? a.name })),
	loadProviders: asyncNoop,
	loadAgents: asyncNoop,
	initializeApp: async () => { set({ isInitialized: true, isConnected: true }); },
	setModel: noop,
	setProvider: noop,
	setAgent: noop,
	setSelectedProvider: noop,
	setCurrentVariant: noop,
	setSettingsDefaultAgent: noop,
	setSettingsDefaultModel: noop,
	setSettingsDefaultVariant: noop,
	setSettingsDefaultFileViewerPreview: noop,
	setSettingsGitmojiEnabled: noop,
	setSettingsMessageStreamTransport: noop,
	setVoiceModeEnabled: noop,
	setVoiceProvider: noop,
	setBrowserVoice: noop,
	setSayVoice: noop,
	setOpenaiVoice: noop,
	setOpenaiApiKey: noop,
	setOpenaiCompatibleUrl: noop,
	setOpenaiCompatibleApiKey: noop,
	setOpenaiCompatibleVoice: noop,
	setOpenaiCompatibleTtsModel: noop,
	setSttProvider: noop,
	setSttServerUrl: noop,
	setSttModel: noop,
	setSttLanguage: noop,
	setSttApiKey: noop,
	setSttSilenceThresholdDb: noop,
	setSttSilenceHoldMs: noop,
	setSttTranscribeOnStop: noop,
	setWasmSttModel: noop,
	setSpeechRate: noop,
	setSpeechPitch: noop,
	setSpeechVolume: noop,
	setShowMessageTTSButtons: noop,
}));

useChatStore.subscribe((state) => {
	const connected = state.connection === 'connected';
	const agents = state.agents.map((a) => ({
		id: a.name,
		name: a.display_name ?? a.name,
		model: a.model,
	}));
	const currentAgent = state.currentAgent;
	useConfigStore.setState({
		isConnected: connected,
		isInitialized: connected,
		agents,
		currentProviderId: state.composerModel ? 'default' : '',
		currentModelId: state.composerModel || '',
		currentAgentName: currentAgent?.name || '',
		providers: state.composerModel ? [{
			id: 'default',
			name: 'Provider',
			models: [{ id: state.composerModel, name: state.composerModel }],
		}] : [],
	});
});
