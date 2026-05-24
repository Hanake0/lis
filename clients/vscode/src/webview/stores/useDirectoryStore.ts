import { create } from 'zustand';

interface DirectoryStore {
	currentDirectory: string | null;
	setCurrentDirectory: (dir: string | null) => void;
}

export const useDirectoryStore = create<DirectoryStore>((set) => ({
	currentDirectory: null,
	setCurrentDirectory: (dir) => set({ currentDirectory: dir }),
}));
