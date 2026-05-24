import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSnippetsStore = create<any>(() => ({
	snippets: [], createSnippet: noop, deleteSnippet: noop, updateSnippet: noop,
}));