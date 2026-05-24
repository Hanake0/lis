import { create } from 'zustand';

interface SelectionStore {
	selectedModel: string;
	selectedProvider: string;
	selectedAgent: string;
	selectedEffort: string;
	setSelectedModel: (model: string) => void;
	setSelectedProvider: (provider: string) => void;
	setSelectedAgent: (agent: string) => void;
	setSelectedEffort: (effort: string) => void;
}

export const useSelectionStore = create<SelectionStore>((set) => ({
	selectedModel: '',
	selectedProvider: '',
	selectedAgent: '',
	selectedEffort: '',
	setSelectedModel: (model) => set({ selectedModel: model }),
	setSelectedProvider: (provider) => set({ selectedProvider: provider }),
	setSelectedAgent: (agent) => set({ selectedAgent: agent }),
	setSelectedEffort: (effort) => set({ selectedEffort: effort }),
}));
