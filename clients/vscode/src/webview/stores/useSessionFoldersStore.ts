import { create } from 'zustand';
const noop: any = () => {};
export const useSessionFoldersStore = create(() => ({ folders: {} }));
