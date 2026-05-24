import { create } from 'zustand';
const noop: any = () => {};
export const useQuotaStore = create<any>((set: any) => ({
	results: [], isLoading: false, lastUpdated: null, error: null,
	displayMode: 'usage', dropdownProviderIds: [],
	selectedModels: [], expandedFamilies: new Set<string>(),
	selectedProviderId: null, autoRefresh: false, refreshIntervalMs: 60000,
	fetchAllQuotas: noop, loadSettings: noop,
	setDisplayMode: (mode: string) => set({ displayMode: mode }),
	toggleFamilyExpanded: noop, setSelectedProvider: noop,
	setDropdownProviderIds: noop, toggleModelSelected: noop,
	applyDefaultSelections: noop, setAutoRefresh: noop, setRefreshInterval: noop,
}));
export function useQuotaAutoRefresh(): void {}