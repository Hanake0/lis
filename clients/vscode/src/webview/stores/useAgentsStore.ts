import { create } from 'zustand';
const noop: any = () => {};
export const useAgentsStore = create<any>(() => ({
	agents: [], builtInAgents: [], loadAgents: noop,
}));
export function filterVisibleAgents(agents: any[]): any[] { return agents; }
export function isAgentBuiltIn(): boolean { return false; }
export function isAgentHidden(): boolean { return false; }
export function reloadOpenCodeConfiguration(): any { return null; }