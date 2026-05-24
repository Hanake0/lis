import { create } from 'zustand';
const noop: any = () => {};
export const useSkillsStore = create(() => ({ skills: [], skillNames: new Set<string>() }));
