import { create } from 'zustand';
const noop: any = () => {};
export const useFilesViewTabsStore = create(() => ({ tabs: [] }));
