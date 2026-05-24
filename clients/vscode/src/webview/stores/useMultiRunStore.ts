import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useMultiRunStore = create<any>(() => ({
	runs: [], isRunning: false,
}));