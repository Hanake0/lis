import { create } from 'zustand';
const noop: any = () => {};
export const useMultiRunStore = create(() => ({ runs: [] }));
