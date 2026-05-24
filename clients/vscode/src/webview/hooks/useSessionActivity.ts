import { useChatStore } from '@/store';

const IDLE_ACTIVITY = { phase: 'idle' as const, parts: [] };
const BUSY_ACTIVITY = { phase: 'working' as const, parts: [] };

export function useCurrentSessionActivity(): any {
	const isTyping = useChatStore((s) => s.isTyping);
	return isTyping ? BUSY_ACTIVITY : IDLE_ACTIVITY;
}
export function useSessionActivity(..._args: any[]): any { return null; }
