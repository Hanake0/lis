import { create } from 'zustand';

interface PermissionStore {
	permissions: unknown[];
}

export const usePermissionStore = create<PermissionStore>(() => ({
	permissions: [],
}));
