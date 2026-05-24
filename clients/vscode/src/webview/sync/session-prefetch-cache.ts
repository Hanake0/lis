export function getSessionPrefetch(_directory: string, _sessionId: string): { cursor?: string; complete?: boolean } | undefined {
	return undefined;
}

export function subscribeSessionPrefetch(_directory: string, _sessionId: string, _notify: () => void): () => void {
	return () => {};
}
