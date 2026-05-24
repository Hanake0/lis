import { create } from 'zustand';
const noop: any = () => {};

export interface InlineCommentDraft {
	path: string;
	line: number;
	text: string;
}

interface InlineCommentDraftStore {
	drafts: InlineCommentDraft[];
	addDraft: (draft: InlineCommentDraft) => void;
	removeDraft: (path: string, line: number) => void;
	clearDrafts: () => void;
}

export const useInlineCommentDraftStore = create<InlineCommentDraftStore>((set) => ({
	drafts: [],
	addDraft: (draft) => set((s) => ({ drafts: [...s.drafts, draft] })),
	removeDraft: (path, line) => set((s) => ({ drafts: s.drafts.filter((d) => d.path !== path || d.line !== line) })),
	clearDrafts: () => set({ drafts: [] }),
}));
