import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import { LisHttpClient } from './http-client';

interface RecordedCall {
	url:     string;
	init?:   RequestInit;
}

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

describe('LisHttpClient', () => {
	const originalFetch = globalThis.fetch;
	let fake: FakeFetch;

	beforeEach(() => {
		fake = new FakeFetch();
		globalThis.fetch = fake.asFetch();
	});

	afterEach(() => {
		globalThis.fetch = originalFetch;
	});

	test('returns status=0 when not configured', async () => {
		const client = new LisHttpClient('', '');
		const res = await client.request({ method: 'GET', path: '/projects' });
		expect(res.status).toBe(0);
		expect(fake.calls.length).toBe(0);
	});

	test('builds URL under /api when path is bare', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		await client.request({ method: 'GET', path: '/projects' });
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/projects');
	});

	test('respects absolute /api paths', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		await client.request({ method: 'GET', path: '/api/events' });
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/events');
	});

	test('appends query params', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		await client.request({ method: 'GET', path: '/git/status', query: { path: '/repo', limit: 5 } });
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/git/status?path=%2Frepo&limit=5');
	});

	test('drops null/undefined query params', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		await client.request({ method: 'GET', path: '/x', query: { a: 'keep', b: null, c: undefined } });
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/x?a=keep');
	});

	test('sends Authorization header when token present', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'my-token');
		await client.request({ method: 'GET', path: '/x' });
		const headers = fake.calls[0].init?.headers as Record<string, string>;
		expect(headers.Authorization).toBe('Bearer my-token');
	});

	test('omits Authorization header when token empty', async () => {
		const client = new LisHttpClient('http://localhost:3010', '');
		await client.request({ method: 'GET', path: '/x' });
		const headers = fake.calls[0].init?.headers as Record<string, string>;
		expect(headers.Authorization).toBeUndefined();
	});

	test('serialises JSON body and sets Content-Type', async () => {
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		await client.request({ method: 'POST', path: '/x', body: { name: 'abc' } });
		const init = fake.calls[0].init!;
		expect(init.body).toBe('{"name":"abc"}');
		const headers = init.headers as Record<string, string>;
		expect(headers['Content-Type']).toBe('application/json');
	});

	test('parses JSON response when content-type matches', async () => {
		fake.respond = () => new Response('{"id":"abc"}', { status: 200, headers: { 'content-type': 'application/json; charset=utf-8' } });
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		const res = await client.request({ method: 'GET', path: '/x' });
		expect(res.status).toBe(200);
		expect((res.body as { id: string }).id).toBe('abc');
	});

	test('returns raw text when content-type is not JSON', async () => {
		fake.respond = () => new Response('plain', { status: 200, headers: { 'content-type': 'text/plain' } });
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		const res = await client.request({ method: 'GET', path: '/x' });
		expect(res.body).toBe('plain');
	});

	test('handles 204 No Content by returning a null body (not empty string)', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		const res = await client.request({ method: 'DELETE', path: '/x' });
		expect(res.status).toBe(204);
		// null lets callers test `if (body)` without false-positives from "" — and
		// matches the GET-by-id semantics for "row absent" used by /api/git/identities/global.
		expect(res.body).toBeNull();
	});

	test('returns status 0 with error message on network failure', async () => {
		globalThis.fetch = async () => { throw new Error('refused'); };
		const client = new LisHttpClient('http://localhost:3010', 'tok');
		const res = await client.request({ method: 'GET', path: '/x' });
		expect(res.status).toBe(0);
		expect((res.body as { error: string }).error).toBe('refused');
	});

	test('updateConfig swaps base origin and token', async () => {
		const client = new LisHttpClient('http://old:1234', 'old');
		client.updateConfig('http://new:5678', 'new');
		await client.request({ method: 'GET', path: '/x' });
		expect(fake.calls[0].url).toBe('http://new:5678/api/x');
		const headers = fake.calls[0].init?.headers as Record<string, string>;
		expect(headers.Authorization).toBe('Bearer new');
	});

	test('strips trailing slashes from base origin', async () => {
		const client = new LisHttpClient('http://localhost:3010///', 'tok');
		await client.request({ method: 'GET', path: '/x' });
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/x');
	});
});
