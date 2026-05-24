import { create } from 'zustand';
export const useAgentsStore = create(() => ({ agents: [] }));
export function filterVisibleAgents(agents: unknown[]): unknown[] { return agents; }
