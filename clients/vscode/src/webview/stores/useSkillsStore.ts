import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSkillsStore = create<any>(() => ({
	skills: [], skillNames: new Set<string>(),
}));