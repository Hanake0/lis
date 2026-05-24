import { create } from 'zustand';
const noop: any = () => {};
export const usePermissionStore = create<any>(() => ({
	permissions: [], setSessionAutoAccept: noop,
}));