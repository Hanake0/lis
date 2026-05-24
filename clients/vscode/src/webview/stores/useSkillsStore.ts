import { create } from 'zustand';
const noop: any = () => {};
export const useSkillsStore = create<any>(() => ({
	skills: [], skillNames: new Set<string>(), loadSkills: noop,
}));