/**
 * REST client used by the extension host. Webviews never call fetch directly —
 * they post a `http_request` envelope to the host (see SidebarWebviewProvider
 * and ChatPanelManager) and the host proxies through here.
 *
 * Configuration (`baseOrigin` + `token`) is mutable: callers swap them at
 * runtime via `updateConfig` when `lis.serverUrl` / `lis.authToken` change.
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

	constructor(baseOrigin: string, token: string) {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
	}

	updateConfig(baseOrigin: string, token: string): void {
		this.baseOrigin = stripTrailingSlash(baseOrigin);
		this.token = token;
	}

	get configuredOrigin(): string {
		return this.baseOrigin;
	}

	async request(req: HttpRequest): Promise<HttpResponse> {
		if (!this.baseOrigin) {
			return { status: 0, body: { error: 'lis.serverUrl is not configured' } };
		}

		const url = this.buildUrl(req.path, req.query);
		const headers: Record<string, string> = {
			Accept: 'application/json',
		};
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
			// Network-level failure (DNS, refused, aborted). Surface as status 0 so the webview
			// can show a connection error rather than crashing on undefined.status.
			return {
				status: 0,
				body: { error: err instanceof Error ? err.message : String(err) },
			};
		}

		const text = await response.text();
		let body: unknown = text;
		// Empty body = no JSON to parse (common for 204 No Content).
		if (text.length > 0) {
			const contentType = response.headers.get('content-type') ?? '';
			if (contentType.includes('json')) {
				try { body = JSON.parse(text); }
				catch { /* leave as raw text */ }
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

function stripTrailingSlash(s: string): string {
	return s.replace(/\/+$/, '');
}
