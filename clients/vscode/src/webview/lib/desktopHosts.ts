/**
 * OC desktop discovers other OC instances on the LAN via Bonjour/mDNS and shows
 * them in the host switcher. Lis runs as a VS Code extension and has no equivalent
 * — we always speak to the single configured `lis.serverUrl`. Stubs return empty
 * data so OC's Header component can render its switcher without crashing.
 */

export interface DesktopHost {
	id:       string;
	name:     string;
	url:      string;
	hostname?: string;
}

const EMPTY: DesktopHost[] = [];

export function getDesktopHosts(): DesktopHost[] { return EMPTY; }

/** Look up a discovered host by id. Always null in Lis. */
export function desktopHostsGet(_id: string): DesktopHost | null { return null; }

/** True when the given URL matches a discovered host. Always false in Lis (no discovery). */
export function locationMatchesHost(_url: string, _host: DesktopHost | string): boolean { return false; }

/**
 * Strip query strings + path from a URL for safe display in the host switcher.
 * Pure helper — works the same in OC and Lis even though we never call it
 * with a discovered-host URL.
 */
export function redactSensitiveUrl(url: string | null | undefined): string {
	if (!url) return '';
	const trimmed = url.trim();
	try {
		const u = new URL(trimmed);
		const host = u.port ? `${u.hostname}:${u.port}` : u.hostname;
		return `${u.protocol}//${host}`;
	} catch {
		return trimmed;
	}
}
