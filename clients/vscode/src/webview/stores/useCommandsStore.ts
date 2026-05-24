import { create } from 'zustand';
export const useCommandsStore = create(() => ({ commands: [] }));
export function isCommandBuiltIn(..._args: any[]): any { return null; }
