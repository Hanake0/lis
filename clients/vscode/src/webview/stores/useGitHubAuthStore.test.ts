import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useGitHubAuthStore } from './useGitHubAuthStore';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_type: 'message', handler: (e: { data: unknown }) => void): void {
		this.handlers.push(handler);
	}
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

class FakeEventSource implements EventSourceLike {
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	constructor(_url: string) { /* noop */ }
	close(): void { /* noop */ }
	addEventListener(_type: string, _handler: (ev: MessageEvent) => void): void { /* noop */ }
}

interface RecordedCall { url: string; init?: RequestInit }

class FakeFetch {
	calls: RecordedCall[] = [];
	respond: (req: { url: string; init?: RequestInit }) => Response = () =>
		new Response('null', { status: 200, headers: { 'content-type': 'application/json' } });

	asFetch(): typeof globalThis.fetch {
		return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
			const url = typeof input === 'string' ? input : input.toString();
			this.calls.push({ url, init });
			return this.respond({ url, init });
		};
	}
}

function jsonResponse(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

describe('useGitHubAuthStore', () => {
	const originalFetch = globalThis.fetch;
	let source: FakeMessageSource;
	let fake:   FakeFetch;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		source = new FakeMessageSource();
		fake   = new FakeFetch();
		globalThis.fetch = fake.asFetch();
		LisRpcClient.install(source, { eventSourceFactory: (url) => new FakeEventSource(url) });
		source.fire({ type: 'lis.init', baseOrigin: 'http://localhost:3010', token: 'tok' });
		useGitHubAuthStore.setState({ status: null, isLoading: false, hasChecked: false });
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('setStatus updates the status and flags as checked', () => {
		useGitHubAuthStore.getState().setStatus({ connected: true, login: 'me' });
		const s = useGitHubAuthStore.getState();
		expect(s.status).toEqual({ connected: true, login: 'me' });
		expect(s.hasChecked).toBe(true);
	});

	test('refreshStatus issues GET /api/github/status and stores the result', async () => {
		fake.respond = () => jsonResponse({
			connected: true,
			login: 'me',
			scopes: 'repo',
			user:     { login: 'me', avatar_url: 'https://avatars/me.png' },
			accounts: [{ id: 'me', login: 'me', avatar_url: 'https://avatars/me.png', is_active: true }],
		});
		const result = await useGitHubAuthStore.getState().refreshStatus();
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/github/status');
		expect(fake.calls[0].init?.method).toBeOneOf(['GET', undefined]);
		// Wire is snake_case; the rpc-client remaps to camelCase so OC consumers see avatarUrl/isActive.
		expect(result).toEqual({
			connected: true,
			login:     'me',
			scopes:    'repo',
			error:     undefined,
			user:      { login: 'me', avatarUrl: 'https://avatars/me.png' },
			accounts:  [{ id: 'me', login: 'me', avatarUrl: 'https://avatars/me.png', isActive: true }],
		});
		const s = useGitHubAuthStore.getState();
		expect(s.status?.connected).toBe(true);
		expect(s.isLoading).toBe(false);
		expect(s.hasChecked).toBe(true);
	});

	test('refreshStatus short-circuits when already checked and not forced', async () => {
		useGitHubAuthStore.setState({ status: { connected: true, login: 'cached' }, hasChecked: true, isLoading: false });
		const result = await useGitHubAuthStore.getState().refreshStatus();
		expect(fake.calls.length).toBe(0);
		expect(result).toEqual({ connected: true, login: 'cached' });
	});

	test('refreshStatus with { force: true } re-fetches even when already checked', async () => {
		useGitHubAuthStore.setState({ status: { connected: true, login: 'cached' }, hasChecked: true, isLoading: false });
		fake.respond = () => jsonResponse({ connected: true, login: 'fresh' });
		const result = await useGitHubAuthStore.getState().refreshStatus(undefined, { force: true });
		expect(fake.calls.length).toBe(1);
		expect(result?.login).toBe('fresh');
	});

	test('refreshStatus stores an error status when the REST call fails', async () => {
		fake.respond = () => jsonResponse({ error: 'token revoked' }, 401);
		const result = await useGitHubAuthStore.getState().refreshStatus();
		expect(result).toBeNull();
		const s = useGitHubAuthStore.getState();
		expect(s.status?.connected).toBe(false);
		expect(s.status?.error).toContain('token revoked');
		expect(s.hasChecked).toBe(true);
		expect(s.isLoading).toBe(false);
	});

	test('refreshStatus delegates to a runtime bridge when one is provided', async () => {
		const runtimeGitHub = {
			authStatus: async () => ({ connected: true, login: 'runtime', scopes: null }),
		};
		const result = await useGitHubAuthStore.getState().refreshStatus(runtimeGitHub);
		expect(fake.calls.length).toBe(0);
		expect(result?.login).toBe('runtime');
	});

	test('concurrent refreshStatus calls return the same resolved value (inflight dedup)', async () => {
		fake.respond = () => jsonResponse({ connected: true, login: 'shared' });
		const [r1, r2] = await Promise.all([
			useGitHubAuthStore.getState().refreshStatus(),
			useGitHubAuthStore.getState().refreshStatus(),
		]);
		expect(r1).toEqual(r2);
		expect(fake.calls.length).toBe(1);
	});
});
