import { create } from 'zustand';
const noop: any = () => {};
export const useMagicPromptsStore = create<any>(() => ({
	prompts: [], templates: [], selectedPromptId: null, setSelectedPromptId: noop,
}));