import { create } from 'zustand';

interface QuotaStore {
	results: unknown[];
	isLoading: boolean;
	lastUpdated: number | null;
	displayMode: 'usage' | 'remaining';
	dropdownProviderIds: string[];
	fetchAllQuotas: () => void;
	loadSettings: () => Promise<void>;
	setDisplayMode: (mode: 'usage' | 'remaining') => void;
}

export const useQuotaStore = create<QuotaStore>((set) => ({
	results: [],
	isLoading: false,
	lastUpdated: null,
	displayMode: 'usage',
	dropdownProviderIds: [],
	fetchAllQuotas: () => {},
	loadSettings: async () => {},
	setDisplayMode: (mode) => set({ displayMode: mode }),
}));

export function useQuotaAutoRefresh(): void {}
