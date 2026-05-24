import { create } from 'zustand';
const noop: any = () => {};
export const useMultiRunStore = create<any>(() => ({
	runs: [], isRunning: false, isLoading: false, createMultiRun: noop,
}));