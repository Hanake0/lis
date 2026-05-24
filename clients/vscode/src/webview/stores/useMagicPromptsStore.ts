import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useMagicPromptsStore = create<any>(() => ({
	prompts: [], templates: [],
}));