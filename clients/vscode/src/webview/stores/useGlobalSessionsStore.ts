import { create } from 'zustand';

export const useGlobalSessionsStore = create(() => ({
	sessions: [],
}));

export function resolveGlobalSessionDirectory(_sessionId: string): string | null {
	return null;
}
