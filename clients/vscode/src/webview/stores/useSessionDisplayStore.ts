import { create } from 'zustand';
const noop: any = () => {};
export const useSessionDisplayStore = create(() => ({ displaySettings: {} }));
