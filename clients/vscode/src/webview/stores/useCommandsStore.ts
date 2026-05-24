import { create } from 'zustand';
const noop: any = () => {};
export const useCommandsStore = create<any>(() => ({
	commands: [], getCommand: () => null, loadCommands: noop,
}));
export function isCommandBuiltIn(): boolean { return false; }