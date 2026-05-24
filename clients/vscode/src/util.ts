export function generateNonce(): string {
	const bytes = new Uint8Array(16);
	crypto.getRandomValues(bytes);
	return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

export function relativeTime(iso: string): string {
	const then = Date.parse(iso);
	if (Number.isNaN(then)) return '';
	const seconds = Math.round((Date.now() - then) / 1000);
	if (seconds < 60)     return `${seconds}s`;
	if (seconds < 3600)   return `${Math.round(seconds / 60)}m`;
	if (seconds < 86400)  return `${Math.round(seconds / 3600)}h`;
	if (seconds < 604800) return `${Math.round(seconds / 86400)}d`;
	return new Date(then).toLocaleDateString();
}
