import { create } from 'zustand';
const noop: any = () => {};
export const useGitIdentitiesStore = create(() => ({ identities: [] }));
