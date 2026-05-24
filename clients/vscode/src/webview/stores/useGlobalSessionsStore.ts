import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGlobalSessionsStore = create<any>(() => ({
	sessions: [], activeSessions: [], globalActiveSessions: [],
	archivedSessions: [], hasLoaded: true,
}));
export function resolveGlobalSessionDirectory(_id: string): string | null { return null; }
export function refreshGlobalSessions(..._a: any[]): any { return null; }