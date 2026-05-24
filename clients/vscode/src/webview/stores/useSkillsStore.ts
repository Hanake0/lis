import { create } from 'zustand';
export const useSkillsStore = create(() => ({ skills: [], skillNames: new Set<string>() }));
