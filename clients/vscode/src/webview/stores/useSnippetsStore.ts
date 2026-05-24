import { create } from 'zustand';
const noop: any = () => {};
export const useSnippetsStore = create<any>(() => ({
	snippets: [], createSnippet: noop, deleteSnippet: noop, updateSnippet: noop,
	loadSnippets: noop, setSnippetDraft: noop, setSelectedSnippet: noop,
}));