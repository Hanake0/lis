import { create } from 'zustand';
const noop: any = () => {};
export const useDirectoryStore = create<any>((set: any) => ({
	currentDirectory: null, directories: [], homeDirectory: null,
	isHomeReady: true,
	setCurrentDirectory: (dir: any) => set({ currentDirectory: dir }),
	setDirectory: noop, addDirectory: noop,
}));