import { create } from 'zustand';
const noop: any = () => {};
export const useSnippetsStore = create(() => ({ snippets: [] }));
