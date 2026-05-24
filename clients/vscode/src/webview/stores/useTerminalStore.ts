import { create } from 'zustand';
const noop: any = () => {};
export const useTerminalStore = create(() => ({ terminals: [] }));
