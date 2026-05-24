import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useAgentsStore = create<any>(() => ({
	agents: [], builtInAgents: [],
}));
export function filterVisibleAgents(agents: unknown[]): unknown[] { return agents; }
export function isAgentBuiltIn(..._a: any[]): any { return false; }
export function isAgentHidden(..._a: any[]): any { return false; }
export function reloadOpenCodeConfiguration(..._a: any[]): any { return null; }