/**
 * Lis-flavoured config store — drives the Composer's model picker, effort
 * dropdown, agent picker and the lightweight provider-icon chrome. The OC
 * original is ~2200 LOC covering voice, STT, persistence, model variants,
 * etc.; we only port the **providers / agents** slice that ModelControls,
 * ScheduledTaskEditorDialog, and TodoSendDialog actually read.
 *
 * Data sources:
 *   - `loadProviders` → `rpc.getProviders()` → normalises Provider SDK shape
 *     (models object → array) and stores it in `providers`. `defaultProviders`
 *     mirrors the API's `default` block so the composer can resolve a
 *     fallback model/agent without crawling the agent table.
 *   - `loadAgents` is a delegate to the global useChatStore (already loaded
 *     by chat-panel-bridge / sidebar bridge from `GET /api/agents`).
 *
 * Selection (`currentProviderId`, `currentModelId`, `currentVariant`,
 * `currentAgentName`) is local UI state — flipping them updates the
 * composer overrides for the next message send. Persistence to the
 * backend goes through `setChatAgent` / `saveChatOverrides`, not here.
 */
import { create } from 'zustand';
import { useChatStore } from '@/store';
import { LisRpcClient } from '@/lib/lis/rpc-client';

export interface ProviderModel {
	id:           string;
	name:         string;
	release_date?: string | null;
	attachment?:  { image: boolean; video: boolean; audio: boolean };
	reasoning?:   { enabled: boolean; supported_efforts: string[] };
	cost?:        { input: number; output: number; cache_read: number; cache_write: number };
	limit?:       { context: number; output: number };
	experimental?: boolean;
}

export interface ModelMetadata extends ProviderModel {}

export interface ProviderEntry {
	id:     string;
	name:   string;
	models: ProviderModel[];
}

export interface AgentEntry {
	id:       string;
	name:     string;
	model?:   string;
	provider?: string;
}

interface ProviderDefaults {
	model: string | null;
	agent: string | null;
}

const noop:      any = () => {};
const asyncNoop: any = async () => {};

let _loadProvidersInFlight: Promise<void> | null = null;

export const useConfigStore = create<any>((set: any, get: any) => ({
	isInitialized: false,
	isConnected:   false,
	providers:        [] as ProviderEntry[],
	defaultProviders: { model: null, agent: null } as ProviderDefaults,
	agents:           [] as AgentEntry[],
	// ModelPickerList calls `.get('providerId/modelId')` on this — Map shape
	// is non-negotiable. Lis doesn't pull from models.dev today so this stays
	// empty; once a remote metadata fetcher lands it populates the same Map.
	modelsMetadata:   new Map<string, any>(),
	currentAgentName:   '',
	currentModelId:     '',
	currentProviderId:  '',
	currentVariant:     '',
	selectedProviderId: '',
	settingsDefaultAgent:   '',
	settingsDefaultVariant: '',
	settingsDefaultFileViewerPreview: false,
	settingsGitmojiEnabled:           false,
	settingsMessageStreamTransport:  'auto',
	voiceModeEnabled: false,
	voiceProvider:    'browser',
	browserVoice:           '',
	sayVoice:               '',
	openaiVoice:            '',
	openaiApiKey:           '',
	openaiCompatibleUrl:    '',
	openaiCompatibleApiKey: '',
	openaiCompatibleVoice:  '',
	openaiCompatibleTtsModel: '',
	sttProvider:        'browser',
	sttServerUrl:       '',
	sttModel:           '',
	sttLanguage:        '',
	sttApiKey:          '',
	sttSilenceThresholdDb: -40,
	sttSilenceHoldMs:     1500,
	sttTranscribeOnStop:   false,
	wasmSttModel:         '',
	speechRate:    1,
	speechPitch:   1,
	speechVolume:  1,
	showMessageTTSButtons: false,
	_contextBudget: 0,

	getCurrentModel: () => {
		const state = useConfigStore.getState();
		const provider = state.providers.find((p: ProviderEntry) => p.id === state.currentProviderId);
		if (!provider) {
			// Fall back to the contextData budget mirror so the context panel
			// still renders a "Model" label before providers load.
			if (!state.currentModelId) return undefined;
			const limit = state._contextBudget > 0 ? { context: state._contextBudget, output: 0 } : undefined;
			return { id: state.currentModelId, name: state.currentModelId, limit };
		}
		const model = provider.models.find((m: ProviderModel) => m.id === state.currentModelId);
		return model ?? undefined;
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

	getModelMetadata: (providerId?: string, modelId?: string): ProviderModel | null => {
		if (!providerId || !modelId) return null;
		const provider = get().providers.find((p: ProviderEntry) => p.id === providerId);
		if (!provider) return null;
		return provider.models.find((m: ProviderModel) => m.id === modelId) ?? null;
	},

	getVisibleAgents: () =>
		useChatStore.getState().agents.map((a) => ({ id: a.name, name: a.display_name ?? a.name })),

	loadProviders: async (): Promise<void> => {
		if (_loadProvidersInFlight) return _loadProvidersInFlight;
		const run = (async () => {
			try {
				const rpc = LisRpcClient.instance();
				const result = await rpc.getProviders();
				const providers: ProviderEntry[] = (result.providers ?? []).map((p) => ({
					id:   p.id,
					name: p.name,
					// API ships models as a Record<id, model>; OC components want an array.
					models: Object.values(p.models ?? {}),
				}));
				const defaults: ProviderDefaults = result.default ?? { model: null, agent: null };
				set((prev: any) => {
					const next: any = { providers, defaultProviders: defaults };
					// Resolve currentProviderId: if the user (or the agent-loader)
					// has already picked a model but the provider isn't set yet,
					// walk providers to find which one owns it. This also covers
					// the cold start where defaults.model lands first.
					const candidateModelId = prev.currentModelId || defaults.model || null;
					if (candidateModelId && !prev.currentProviderId) {
						for (const provider of providers) {
							if (provider.models.find((m) => m.id === candidateModelId)) {
								next.currentProviderId  = provider.id;
								next.currentModelId     = candidateModelId;
								next.selectedProviderId = provider.id;
								break;
							}
						}
					}
					if (!prev.currentAgentName && defaults.agent) {
						next.currentAgentName = defaults.agent;
					}
					return next;
				});
			} catch (err) {
				console.warn('[useConfigStore] loadProviders failed:', err);
			}
		})();
		_loadProvidersInFlight = run;
		try { await run; } finally { _loadProvidersInFlight = null; }
	},

	loadAgents: asyncNoop,

	initializeApp: async () => {
		await get().loadProviders();
		set({ isInitialized: true, isConnected: true });
	},

	setModel: (modelId: string) => set({ currentModelId: modelId }),

	setProvider: (providerId: string) => set({ currentProviderId: providerId, selectedProviderId: providerId }),

	setSelectedProvider: (providerId: string) => set({ selectedProviderId: providerId }),

	setAgent: (agentName: string | undefined) => set({ currentAgentName: agentName ?? '' }),

	setCurrentVariant:                noop,
	setSettingsDefaultAgent:          noop,
	setSettingsDefaultModel:          noop,
	setSettingsDefaultVariant:        noop,
	setSettingsDefaultFileViewerPreview: noop,
	setSettingsGitmojiEnabled:        noop,
	setSettingsMessageStreamTransport: noop,
	setVoiceModeEnabled:              noop,
	setVoiceProvider:                 noop,
	setBrowserVoice:                  noop,
	setSayVoice:                      noop,
	setOpenaiVoice:                   noop,
	setOpenaiApiKey:                  noop,
	setOpenaiCompatibleUrl:           noop,
	setOpenaiCompatibleApiKey:        noop,
	setOpenaiCompatibleVoice:         noop,
	setOpenaiCompatibleTtsModel:      noop,
	setSttProvider:                   noop,
	setSttServerUrl:                  noop,
	setSttModel:                      noop,
	setSttLanguage:                   noop,
	setSttApiKey:                     noop,
	setSttSilenceThresholdDb:         noop,
	setSttSilenceHoldMs:              noop,
	setSttTranscribeOnStop:           noop,
	setWasmSttModel:                  noop,
	setSpeechRate:                    noop,
	setSpeechPitch:                   noop,
	setSpeechVolume:                  noop,
	setShowMessageTTSButtons:         noop,
}));

// Mirror useChatStore → useConfigStore — keeps `agents`, the connection flag,
// the composer-model selection and the context budget in sync until the OC
// agentsStore / contextStore ports land in 15c.5.
useChatStore.subscribe((state) => {
	const connected = state.connection === 'connected';
	const agents = state.agents.map((a) => ({
		id:    a.name,
		name:  a.display_name ?? a.name,
		model: a.model,
	}));
	const model  = state.composerModel || '';
	const budget = state.contextData?.budget ?? 0;
	const currentAgent = state.currentAgent;
	useConfigStore.setState((prev: any) => {
		const next: any = {
			isConnected:     connected,
			isInitialized:   prev.isInitialized || connected,
			agents,
			_contextBudget:  budget,
			currentAgentName: currentAgent?.name || prev.currentAgentName,
		};
		// Only adopt the composer-side model if the providers list hasn't
		// resolved one yet — otherwise the user's explicit selection sticks.
		// If we already have providers loaded, also resolve the provider id
		// for this model so the model picker shows the name (not "Not
		// selected").
		if (model && !prev.currentModelId) {
			next.currentModelId = model;
			if (!prev.currentProviderId && Array.isArray(prev.providers)) {
				for (const provider of prev.providers) {
					if ((provider.models ?? []).find((m: ProviderModel) => m.id === model)) {
						next.currentProviderId  = provider.id;
						next.selectedProviderId = provider.id;
						break;
					}
				}
			}
		}
		return next;
	});
});

// Bootstrap is wired from `webview/index.ts` so the call happens
// deterministically after `LisRpcClient.install` regardless of which
// component first imports this module (the chat panel touches it lazily
// via ChatInput, which mounts AFTER rpc.ready() has already resolved).
