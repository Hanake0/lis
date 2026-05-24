import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useSessionFoldersStore = create<any>(() => ({
	folders: {}, foldersByProject: {},
	getFolders: () => [], getFolderById: () => null, createFolder: noop,
	deleteFolder: noop, renameFolder: noop, moveSessionToFolder: noop,
	removeSessionFromFolder: noop, reorderFolders: noop,
	getFolderSessions: () => [], getSessionFolder: () => null, loadFolders: noop,
}));