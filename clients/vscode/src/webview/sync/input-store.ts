import { create } from 'zustand';

export interface SyntheticContextPart {
	text: string;
	synthetic?: boolean;
}

interface InputStore {
	inputText: string;
	setInputText: (text: string) => void;
}

export const useInputStore = create<InputStore>((set) => ({
	inputText: '',
	setInputText: (text) => set({ inputText: text }),
}));
