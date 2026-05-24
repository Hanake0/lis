import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSelectionStore = create<any>((set: any) => ({
	selectedModel: '', selectedProvider: '', selectedAgent: '', selectedEffort: '',
	selectedVariant: '', selectedEditPermissionMode: 'ask',
	setSelectedModel: (m: string) => set({ selectedModel: m }),
	setSelectedProvider: (p: string) => set({ selectedProvider: p }),
	setSelectedAgent: (a: string) => set({ selectedAgent: a }),
	setSelectedEffort: (e: string) => set({ selectedEffort: e }),
	setSelectedVariant: noop, setSelectedEditPermissionMode: noop,
	resetSelections: noop,
}));