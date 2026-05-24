import { useRef, useCallback } from 'react';

export type ContentChangeReason = 'message' | 'permission' | 'status' | 'resize';
export interface AnimationHandlers {
	onAnimationStart?: () => void;
	onAnimationComplete?: () => void;
}

interface UseChatAutoFollowParams {
	currentSessionId: string | null;
	sessionMessageCount: number;
	sessionIsWorking: boolean;
	isMobile: boolean;
	onActiveTurnChange?: (turnId: string | null) => void;
}

export function useChatAutoFollow(_params: UseChatAutoFollowParams) {
	const scrollRef = useRef<HTMLDivElement | null>(null);

	return {
		scrollRef,
		notifyContentChange: useCallback((_reason?: ContentChangeReason) => {}, []),
		getAnimationHandlers: useCallback((_messageId: string): AnimationHandlers => ({}), []),
		goToBottom: useCallback((_behavior?: 'smooth' | 'instant') => {}, []),
		releaseAutoFollow: useCallback(() => {}, []),
		restoreSnapshot: useCallback(async () => {}, []),
		isPinned: false,
		isFollowingProgrammatically: false,
		showScrollButton: false,
	};
}
