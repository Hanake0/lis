import { create } from 'zustand';
const noop: any = () => {};
export const useSkillsCatalogStore = create<any>(() => ({
	skills: [], isLoading: false, isScanning: false, isInstalling: false, error: null,
	fetchSkills: noop, installSkill: noop, scanRepo: noop, loadCatalog: noop, installSkills: noop,
}));