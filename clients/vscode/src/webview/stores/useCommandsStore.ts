import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useCommandsStore = create<any>(() => ({
	commands: [], getCommand: () => null,
}));
export function isCommandBuiltIn(..._a: any[]): any { return false; }