import { create } from 'zustand';
const noop: any = () => {};
export const useActiveNowStore = create(() => ({ activeNow: [] }));
