import { create } from 'zustand';
const noop: any = () => {};
export const useDesktopSshStore = create(() => ({ connections: [] }));
