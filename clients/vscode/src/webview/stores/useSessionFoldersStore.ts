import { create } from 'zustand';
const noop: any = () => {};
export const useSessionFoldersStore = create<any>(() => ({
	folders: {}, foldersByProject: {}, foldersMap: {},
	collapsedFolderIds: new Set<string>(),
	getFolders: () => [], getFolderById: () => null, getFoldersForScope: () => [],
	createFolder: noop, deleteFolder: noop, renameFolder: noop,
	moveSessionToFolder: noop, removeSessionFromFolder: noop,
	addSessionToFolder: noop, addSessionsToFolder: noop,
	removeSessionsFromFolders: noop, reorderFolders: noop,
	toggleFolderCollapse: noop, cleanupSessions: noop,
	getFolderSessions: () => [], getSessionFolder: () => null,
	getSessionFolderId: () => null, loadFolders: noop,
}));