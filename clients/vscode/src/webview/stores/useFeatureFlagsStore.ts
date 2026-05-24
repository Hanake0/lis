import { create } from 'zustand';

interface FeatureFlagsStore {
	planModeEnabled: boolean;
	setPlanModeEnabled: (v: boolean) => void;
}

export const useFeatureFlagsStore = create<FeatureFlagsStore>((set) => ({
	planModeEnabled: false,
	setPlanModeEnabled: (v) => set({ planModeEnabled: v }),
}));
