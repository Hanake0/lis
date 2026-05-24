import { create } from 'zustand';
const noop: any = () => {};
export const useSkillsCatalogStore = create(() => ({ skills: [] }));
