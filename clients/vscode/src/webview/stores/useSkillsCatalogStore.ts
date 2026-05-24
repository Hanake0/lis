import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSkillsCatalogStore = create<any>(() => ({
	skills: [], isLoading: false, error: null, fetchSkills: asyncNoop, installSkill: asyncNoop,
}));