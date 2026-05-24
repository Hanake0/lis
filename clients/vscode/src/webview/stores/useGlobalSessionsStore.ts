import { create } from 'zustand';
const noop: any = () => {};

export const useGlobalSessionsStore = create(() => ({
	sessions: [],
}));

export function resolveGlobalSessionDirectory(_sessionId: string): string | null {
	return null;
}
export function refreshGlobalSessions(..._args: any[]): any { return null; }
