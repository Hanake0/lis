import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export interface SyntheticContextPart { text: string; synthetic?: boolean; }
export const useInputStore = create<any>((set: any) => ({
	inputText: '', attachedFiles: [], syntheticParts: [],
	draftText: '', draftAttachedFiles: [], pendingVSCodeFiles: [],
	setInputText: (text: string) => set({ inputText: text }),
	setAttachedFiles: noop, addAttachedFile: noop, removeAttachedFile: noop,
	clearAttachedFiles: noop, setSyntheticParts: noop,
	setDraftText: noop, setDraftAttachedFiles: noop,
	setPendingVSCodeFiles: noop, clearPendingVSCodeFiles: noop,
	loadDraft: noop, saveDraft: noop,
}));