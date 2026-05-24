import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useUpdateStore = create<any>(() => ({
	checkForUpdates: async () => undefined,
	available: false,
	runtimeType: 'vscode',
	info: null,
	downloading: false,
	downloaded: false,
	progress: null,
	error: null,
	downloadUpdate: asyncNoop,
	restartToUpdate: noop,
}));
