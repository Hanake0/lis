import { create } from 'zustand';
const noop: any = () => {};
export const useAgentsStore = create(() => ({ agents: [] }));
export function filterVisibleAgents(agents: unknown[]): unknown[] { return agents; }
export function isAgentBuiltIn(..._args: any[]): any { return null; }
export function isAgentHidden(..._args: any[]): any { return null; }
export function reloadOpenCodeConfiguration(..._args: any[]): any { return null; }
