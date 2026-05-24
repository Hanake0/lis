/**
 * Configuration helpers for the Lis extension.
 *
 * `lis.serverUrl` is the **base origin** (e.g. `http://localhost:3010`).
 * From it we derive:
 *   - REST base: `${origin}/api`
 *   - WebSocket: `${origin → ws}/web/ws`     (legacy, removed in Stage 13)
 *   - SSE events: `${origin}/api/events`     (Stage 9+)
 *
 * Legacy installs may have a full WS URL stored (`ws://host:port/web/ws`).
 * `normalizeBaseOrigin` collapses any input to the canonical `proto://host[:port]`.
 */

export const DEFAULT_SERVER_URL = 'http://localhost:3010';

/**
 * Collapse any user-supplied URL to the canonical base origin.
 *
 * Examples:
 *   ws://localhost:3010/web/ws   → http://localhost:3010
 *   wss://lis.example.com/web/ws → https://lis.example.com
 *   http://localhost:3010/api    → http://localhost:3010
 *   localhost:3010               → http://localhost:3010
 *
 * Returns the trimmed input unchanged if it can't be parsed.
 */
export function normalizeBaseOrigin(raw: string): string {
	const trimmed = (raw ?? '').trim();
	if (!trimmed) return '';

	// Map ws→http for URL parsing, remember intent (only if the user wrote ws explicitly).
	const wsToHttp = trimmed.replace(/^wss:/i, 'https:').replace(/^ws:/i, 'http:');
	const withProtocol = /^https?:\/\//i.test(wsToHttp) ? wsToHttp : `http://${wsToHttp}`;

	try {
		const u = new URL(withProtocol);
		return `${u.protocol}//${u.host}`;
	} catch {
		return trimmed;
	}
}

/**
 * Derive the WebSocket URL for `LisClient` from a base origin.
 * Legacy path — removed once Stage 13 lands and we delete /web/ws.
 */
export function deriveWsUrl(baseOrigin: string): string {
	const base = normalizeBaseOrigin(baseOrigin);
	if (!base) return '';
	const wsProto = base.startsWith('https:') ? 'wss:' : 'ws:';
	const host = base.replace(/^https?:\/\//, '');
	return `${wsProto}//${host}/web/ws`;
}

/** Future REST base derived from origin. Kept here so config logic stays in one place. */
export function deriveApiBase(baseOrigin: string): string {
	const base = normalizeBaseOrigin(baseOrigin);
	return base ? `${base}/api` : '';
}

/** Future SSE event-stream URL derived from origin. */
export function deriveEventsUrl(baseOrigin: string): string {
	const base = normalizeBaseOrigin(baseOrigin);
	return base ? `${base}/api/events` : '';
}

export interface LisConfig {
	/** Canonical base origin — always normalized. Never empty when `isConfigured` is true. */
	baseOrigin: string;
	token: string;
	verbose: boolean;
	/** True when serverUrl is set (a default counts) and a token is present. */
	isConfigured: boolean;
	/** True when the raw stored serverUrl was in the legacy `ws://…/web/ws` shape. */
	isLegacyUrl: boolean;
}

interface ConfigSource {
	get<T>(key: string, defaultValue: T): T;
}

/** Read raw config from any source (vscode.workspace.getConfiguration or a stub for tests). */
export function readConfigFrom(source: ConfigSource): LisConfig {
	const rawUrl  = source.get<string>('serverUrl', DEFAULT_SERVER_URL);
	const token   = source.get<string>('authToken', '');
	const verbose = source.get<boolean>('verboseLogging', false);

	const baseOrigin = normalizeBaseOrigin(rawUrl || DEFAULT_SERVER_URL);
	const isLegacyUrl = /^wss?:\/\//i.test((rawUrl ?? '').trim()) || /\/web\/ws\b/.test(rawUrl ?? '');

	return {
		baseOrigin,
		token,
		verbose,
		isConfigured: Boolean(baseOrigin) && Boolean(token),
		isLegacyUrl,
	};
}
