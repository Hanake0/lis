import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import type { EventSourceLike } from '@/lib/lis/event-channel';
import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { useGitIdentitiesStore } from './useGitIdentitiesStore';

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_t: 'message', h: (e: { data: unknown }) => void): void { this.handlers.push(h); }
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

class FakeEventSource implements EventSourceLike {
	onopen:  ((ev: Event) => void) | null = null;
	onerror: ((ev: Event) => void) | null = null;
	constructor(_url: string) { /* noop */ }
	close(): void { /* noop */ }
	addEventListener(): void { /* noop */ }
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

function makeDto(over: Record<string, unknown> = {}): Record<string, unknown> {
	return {
		id:           'profile-1',
		name:         'Work',
		user_name:    'Octocat',
		user_email:   'octo@gh.com',
		auth_type:    'ssh',
		ssh_key_path: '/home/u/.ssh/id_ed25519',
		host:         'github.com',
		color:        'purple',
		icon:         'branch',
		created_at:   '2026-01-01T00:00:00Z',
		updated_at:   '2026-01-01T00:00:00Z',
		...over,
	};
}

describe('useGitIdentitiesStore', () => {
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
		useGitIdentitiesStore.setState({
			selectedProfileId: null, defaultGitIdentityId: null,
			profiles: [], globalIdentity: null, discoveredCredentials: [],
			isLoading: false, lastError: null,
		});
	});

	afterEach(() => { globalThis.fetch = originalFetch; });

	test('loadProfiles GETs /api/git/identities and converts the DTO to a profile', async () => {
		fake.respond = () => jsonResponse([makeDto({ id: 'p1', name: 'Profile' })]);
		const ok = await useGitIdentitiesStore.getState().loadProfiles();
		expect(ok).toBe(true);
		expect(fake.calls[0].url).toBe('http://localhost:3010/api/git/identities');
		const profiles = useGitIdentitiesStore.getState().profiles;
		expect(profiles).toHaveLength(1);
		expect(profiles[0].id).toBe('p1');
		expect(profiles[0].sshKey).toBe('/home/u/.ssh/id_ed25519');
		expect(profiles[0].authType).toBe('ssh');
	});

	test('loadProfiles returns false and stores lastError on failure', async () => {
		fake.respond = () => jsonResponse({ error: 'boom' }, 500);
		const ok = await useGitIdentitiesStore.getState().loadProfiles();
		expect(ok).toBe(false);
		expect(useGitIdentitiesStore.getState().lastError).toContain('boom');
	});

	test('loadGlobalIdentity stores a synthesised "global" profile when set on the host', async () => {
		fake.respond = () => jsonResponse({ user_name: 'Octo Cat', user_email: 'octo@gh.com', ssh_command: 'ssh -i ~/.ssh/id_ed25519' });
		const ok = await useGitIdentitiesStore.getState().loadGlobalIdentity();
		expect(ok).toBe(true);
		const g = useGitIdentitiesStore.getState().globalIdentity;
		expect(g?.id).toBe('global');
		expect(g?.userName).toBe('Octo Cat');
		expect(g?.authType).toBe('ssh');
		expect(g?.sshKey).toBe('~/.ssh/id_ed25519');
	});

	test('loadGlobalIdentity sets globalIdentity to null when backend returns 204', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		const ok = await useGitIdentitiesStore.getState().loadGlobalIdentity();
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().globalIdentity).toBeNull();
	});

	test('loadDiscoveredCredentials always succeeds with an empty list (Lis has no discovery)', async () => {
		const ok = await useGitIdentitiesStore.getState().loadDiscoveredCredentials();
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().discoveredCredentials).toEqual([]);
		expect(fake.calls.length).toBe(0);
	});

	test('loadDefaultGitIdentityId reads the pointer', async () => {
		fake.respond = () => jsonResponse({ external_id: 'p1' });
		const ok = await useGitIdentitiesStore.getState().loadDefaultGitIdentityId();
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().defaultGitIdentityId).toBe('p1');
	});

	test('setDefaultGitIdentityId optimistically updates and PUTs the pointer', async () => {
		fake.respond = () => new Response(null, { status: 204 });
		const ok = await useGitIdentitiesStore.getState().setDefaultGitIdentityId('p1');
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().defaultGitIdentityId).toBe('p1');
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body).toEqual({ external_id: 'p1' });
		expect(fake.calls[0].init?.method).toBe('PUT');
	});

	test('setDefaultGitIdentityId rolls back when the PUT fails', async () => {
		useGitIdentitiesStore.setState({ defaultGitIdentityId: 'old' });
		fake.respond = () => jsonResponse({ error: 'denied' }, 403);
		const ok = await useGitIdentitiesStore.getState().setDefaultGitIdentityId('new');
		expect(ok).toBe(false);
		expect(useGitIdentitiesStore.getState().defaultGitIdentityId).toBe('old');
	});

	test('createProfile POSTs the snake_case payload and appends the returned profile', async () => {
		fake.respond = () => jsonResponse(makeDto({ id: 'created-id', name: 'New' }), 201);
		const ok = await useGitIdentitiesStore.getState().createProfile({
			name: 'New', userName: 'u', userEmail: 'u@x.io', authType: 'ssh',
		});
		expect(ok).toBe(true);
		const body = JSON.parse(fake.calls[0].init!.body as string);
		expect(body.user_name).toBe('u');
		expect(body.user_email).toBe('u@x.io');
		expect(body.auth_type).toBe('ssh');
		// Defaults for color/icon are filled in by the store.
		expect(body.color).toBe('keyword');
		expect(body.icon).toBe('branch');
		expect(useGitIdentitiesStore.getState().profiles.map(p => p.id)).toContain('created-id');
	});

	test('updateProfile optimistically merges then PATCHes', async () => {
		useGitIdentitiesStore.setState({
			profiles: [{ id: 'p1', name: 'Old', userName: 'u', userEmail: 'u@x.io' }],
		});
		fake.respond = () => jsonResponse(makeDto({ id: 'p1', name: 'Renamed' }));

		const ok = await useGitIdentitiesStore.getState().updateProfile('p1', { name: 'Renamed' });
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().profiles[0].name).toBe('Renamed');
		const init = fake.calls[0].init!;
		expect(init.method).toBe('PATCH');
		expect(JSON.parse(init.body as string)).toEqual({ name: 'Renamed' });
	});

	test('updateProfile rolls back when the PATCH fails', async () => {
		useGitIdentitiesStore.setState({
			profiles: [{ id: 'p1', name: 'Stable', userName: 'u', userEmail: 'u@x.io' }],
		});
		fake.respond = () => jsonResponse({ error: 'conflict' }, 409);

		const ok = await useGitIdentitiesStore.getState().updateProfile('p1', { name: 'Renamed' });
		expect(ok).toBe(false);
		expect(useGitIdentitiesStore.getState().profiles[0].name).toBe('Stable');
	});

	test('deleteProfile optimistically removes and DELETEs', async () => {
		useGitIdentitiesStore.setState({
			profiles: [{ id: 'p1', name: 'Work', userName: 'u', userEmail: 'u@x.io' }],
			selectedProfileId: 'p1',
		});
		fake.respond = () => new Response(null, { status: 204 });

		const ok = await useGitIdentitiesStore.getState().deleteProfile('p1');
		expect(ok).toBe(true);
		expect(useGitIdentitiesStore.getState().profiles).toEqual([]);
		expect(useGitIdentitiesStore.getState().selectedProfileId).toBeNull();
	});

	test('deleteProfile rolls back the optimistic removal when the DELETE fails', async () => {
		useGitIdentitiesStore.setState({
			profiles: [{ id: 'p1', name: 'Work', userName: 'u', userEmail: 'u@x.io' }],
		});
		fake.respond = () => jsonResponse({ error: 'busy' }, 409);

		const ok = await useGitIdentitiesStore.getState().deleteProfile('p1');
		expect(ok).toBe(false);
		expect(useGitIdentitiesStore.getState().profiles).toHaveLength(1);
	});

	test('getProfileById returns the global identity for the magic "global" id', () => {
		const global = { id: 'global', name: 'Global', userName: 'g', userEmail: 'g@x.io' };
		useGitIdentitiesStore.setState({ globalIdentity: global });
		expect(useGitIdentitiesStore.getState().getProfileById('global')).toBe(global);
	});

	test('getProfileById returns a custom profile by id, or undefined when missing', () => {
		const p = { id: 'p1', name: 'P1', userName: 'u', userEmail: 'u@x.io' };
		useGitIdentitiesStore.setState({ profiles: [p] });
		expect(useGitIdentitiesStore.getState().getProfileById('p1')).toBe(p);
		expect(useGitIdentitiesStore.getState().getProfileById('absent')).toBeUndefined();
	});

	test('getUnimportedCredentials filters out token profiles already present for that host', () => {
		useGitIdentitiesStore.setState({
			profiles: [
				{ id: 'p1', name: 'GH', userName: 'u', userEmail: 'u@x.io', authType: 'token', host: 'github.com' },
			],
			discoveredCredentials: [
				{ host: 'github.com', username: 'u' },
				{ host: 'gitlab.com', username: 'u' },
			],
		});
		const remaining = useGitIdentitiesStore.getState().getUnimportedCredentials();
		expect(remaining.map(c => c.host)).toEqual(['gitlab.com']);
	});

	test('setSelectedProfile updates only the selected id', () => {
		useGitIdentitiesStore.getState().setSelectedProfile('x');
		expect(useGitIdentitiesStore.getState().selectedProfileId).toBe('x');
		useGitIdentitiesStore.getState().setSelectedProfile(null);
		expect(useGitIdentitiesStore.getState().selectedProfileId).toBeNull();
	});
});
