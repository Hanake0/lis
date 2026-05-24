import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useDirectoryStore = create<any>((set: any) => ({
	currentDirectory: null as string | null,
	directories: [],
	setCurrentDirectory: (dir: string | null) => set({ currentDirectory: dir }),
	addDirectory: noop,
}));