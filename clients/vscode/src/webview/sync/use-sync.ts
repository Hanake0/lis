export function useSync() {
	return {
		ensureSessionRenderable: async (_sessionId: string) => {},
		loadMore: async (_sessionId: string) => {},
		hasMore: (_sessionId: string) => false,
		isLoading: (_sessionId: string) => false,
	};
}
