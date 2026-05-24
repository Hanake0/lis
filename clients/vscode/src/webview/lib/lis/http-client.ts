/**
 * REST client used by the webview. Runs in the webview's browser context — the
 * webview's CSP `connect-src` is configured to include the user's `lis.serverUrl`
 * by the providers, so `fetch()` works directly. No extension-host proxying.
 *
 * Configuration (`baseOrigin` + `token`) flows in from the extension host via
 * the initial `init` postMessage; subsequent config changes arrive as
 * `config_update`. Callers swap the values via `updateConfig`.
 */

export interface HttpRequest {
	method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
	path: string;            // e.g. '/projects' — leading slash optional, base is appended automatically
	query?: Record<string, string | number | boolean | null | undefined>;
	body?: unknown;
	signal?: AbortSignal;
}

export interface HttpResponse {
	status: number;
	body: unknown;           // parsed JSON when possible; raw text otherwise
}

export class LisHttpClient {
	private baseOrigin: string;
	private token: string;

	constructor(baseOrigin = '', token = '') {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
	}

	updateConfig(baseOrigin: string, token: string): void {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
	}

	get configuredOrigin(): string { return this.baseOrigin; }
	get configuredToken():  string { return this.token; }

	async request(req: HttpRequest): Promise<HttpResponse> {
		if (!this.baseOrigin) {
			return { status: 0, body: { error: 'lis.serverUrl is not configured' } };
		}

		const url = this.buildUrl(req.path, req.query);
		const headers: Record<string, string> = { Accept: 'application/json' };
		if (this.token) headers.Authorization = `Bearer ${this.token}`;
		if (req.body !== undefined) headers['Content-Type'] = 'application/json';

		let response: Response;
		try {
			response = await fetch(url, {
				method: req.method,
				headers,
				body: req.body === undefined ? undefined : JSON.stringify(req.body),
				signal: req.signal,
			});
		} catch (err) {
			return { status: 0, body: { error: err instanceof Error ? err.message : String(err) } };
		}

		const text = await response.text();
		let body: unknown = text;
		if (text.length > 0) {
			const contentType = response.headers.get('content-type') ?? '';
			if (contentType.includes('json')) {
				try { body = JSON.parse(text); } catch { /* leave as raw text */ }
			}
		}
		return { status: response.status, body };
	}

	private buildUrl(path: string, query?: HttpRequest['query']): string {
		// `path` accepts either `/projects` (relative to /api) or `/api/projects` (absolute) so
		// callers can target either the typed REST surface or a one-off raw path.
		const normalized = path.startsWith('/api/') ? path : `/api${path.startsWith('/') ? path : `/${path}`}`;
		const url = new URL(`${this.baseOrigin}${normalized}`);

		if (query) {
			for (const [key, value] of Object.entries(query)) {
				if (value === null || value === undefined) continue;
				url.searchParams.append(key, String(value));
			}
		}
		return url.toString();
	}
}

function stripTrailingSlash(s: string): string { return s.replace(/\/+$/, ''); }
