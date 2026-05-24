import { create } from 'zustand';
const noop: any = () => {};
export const useMagicPromptsStore = create(() => ({ prompts: [] }));
