import { create } from 'zustand';
const noop: any = () => {};
export interface InlineCommentDraft { path: string; line: number; text: string; }
export const useInlineCommentDraftStore = create<any>((set: any) => ({
	drafts: [] as InlineCommentDraft[],
	addDraft: noop, removeDraft: noop, clearDrafts: noop, consumeDrafts: () => [],
}));