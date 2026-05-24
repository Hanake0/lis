export function getSafeStorage(): Storage | null {
	try { return window.localStorage; } catch { return null; }
}
