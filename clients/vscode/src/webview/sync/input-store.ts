import { create } from 'zustand';
const noop: any = () => {};
export interface SyntheticContextPart { text: string; synthetic?: boolean; }
export const useInputStore = create<any>((set: any) => ({
	inputText: '', attachedFiles: [], syntheticParts: [],
	draftText: '', draftAttachedFiles: [], pendingVSCodeFiles: [],
	pendingInputText: null, pendingSyntheticParts: null, activeEditorFile: null,
	drafts: {} as Record<string, any[]>,
	setInputText: (text: string) => set({ inputText: text }),
	setAttachedFiles: noop, addAttachedFile: noop, removeAttachedFile: noop,
	clearAttachedFiles: noop, setSyntheticParts: noop,
	setDraftText: noop, setDraftAttachedFiles: noop,
	setPendingVSCodeFiles: noop, clearPendingVSCodeFiles: noop,
	loadDraft: noop, saveDraft: noop,
	setPendingInputText: noop, consumePendingInputText: () => null,
	consumePendingSyntheticParts: () => null, setPendingSyntheticParts: noop,
	addVSCodeFileAttachment: noop, addVSCodeSelectionAttachment: noop,
}));