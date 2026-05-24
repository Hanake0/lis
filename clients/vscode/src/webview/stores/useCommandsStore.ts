import { create } from 'zustand';
const noop: any = () => {};
export const useCommandsStore = create(() => ({ commands: [] }));
export function isCommandBuiltIn(..._args: any[]): any { return null; }
