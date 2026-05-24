import { create } from 'zustand';
import type { AttachedFile } from '@/stores/types/sessionTypes';

const MAX_IMAGE_SIZE = 10 * 1024 * 1024; // 10 MB
const ALLOWED_IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];

const noop: any = () => {};

export interface SyntheticContextPart { text: string; synthetic?: boolean; }

const readFileAsDataUrl = (file: File): Promise<string> =>
	new Promise((resolve, reject) => {
		const reader = new FileReader();
		reader.onload = () => resolve(reader.result as string);
		reader.onerror = () => reject(new Error('Failed to read file'));
		reader.readAsDataURL(file);
	});

export const useInputStore = create<any>((set: any, get: any) => ({
	inputText: '', attachedFiles: [] as AttachedFile[], syntheticParts: [],
	draftText: '', draftAttachedFiles: [], pendingVSCodeFiles: [],
	pendingInputText: null as { text: string; mode?: string } | null,
	pendingSyntheticParts: null as SyntheticContextPart[] | null,
	activeEditorFile: null,
	drafts: {} as Record<string, any[]>,
	setInputText: (text: string) => set({ inputText: text }),

	setAttachedFiles: (files: AttachedFile[]) => set({ attachedFiles: files }),

	addAttachedFile: async (file: File) => {
		if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
			throw new Error(`Unsupported file type: ${file.type || 'unknown'}. Only images are supported.`);
		}
		if (file.size > MAX_IMAGE_SIZE) {
			throw new Error(`File too large (${(file.size / 1024 / 1024).toFixed(1)} MB). Maximum is 10 MB.`);
		}
		const dataUrl = await readFileAsDataUrl(file);
		const attachment: AttachedFile = {
			id: `local-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`,
			file,
			filename: file.name,
			mimeType: file.type,
			size: file.size,
			dataUrl,
			source: 'local',
		};
		set({ attachedFiles: [...get().attachedFiles, attachment] });
	},

	removeAttachedFile: (id: string) => {
		set({ attachedFiles: get().attachedFiles.filter((f: AttachedFile) => f.id !== id) });
	},

	clearAttachedFiles: () => set({ attachedFiles: [] }),
	setSyntheticParts: noop,
	setDraftText: noop, setDraftAttachedFiles: noop,
	setPendingVSCodeFiles: noop, clearPendingVSCodeFiles: noop,
	loadDraft: noop, saveDraft: noop,

	setPendingInputText: (text: string, mode?: string) => set({ pendingInputText: { text, mode } }),
	consumePendingInputText: () => {
		const pending = get().pendingInputText;
		if (pending) set({ pendingInputText: null });
		return pending;
	},

	setPendingSyntheticParts: (parts: SyntheticContextPart[]) => set({ pendingSyntheticParts: parts }),
	consumePendingSyntheticParts: () => {
		const parts = get().pendingSyntheticParts;
		if (parts) set({ pendingSyntheticParts: null });
		return parts;
	},

	addVSCodeFileAttachment: async (filePath: string, fileName: string, fileSize?: number) => {
		const attachment: AttachedFile = {
			id: `vscode-file-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`,
			file: new File([], fileName, { type: 'text/plain' }),
			filename: fileName,
			mimeType: 'text/plain',
			size: fileSize ?? 0,
			dataUrl: '',
			source: 'vscode',
			vscodePath: filePath,
			vscodeSource: 'file',
		};
		set({ attachedFiles: [...get().attachedFiles, attachment] });
	},

	addVSCodeSelectionAttachment: async (filePath: string, file: File) => {
		const dataUrl = await readFileAsDataUrl(file);
		const attachment: AttachedFile = {
			id: `vscode-sel-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`,
			file,
			filename: file.name,
			mimeType: file.type || 'text/plain',
			size: file.size,
			dataUrl,
			source: 'vscode',
			vscodePath: filePath,
			vscodeSource: 'selection',
		};
		set({ attachedFiles: [...get().attachedFiles, attachment] });
	},
}));