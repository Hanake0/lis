import { beforeEach, describe, expect, test } from 'bun:test';

import { LisRpcClient, type MessageSource } from '@/lib/lis/rpc-client';
import { installProjectsSync, useProjectsStore, type ProjectEntry } from './useProjectsStore';

interface PostedMessage {
	type:       string;
	request_id: string;
	method:     string;
	path:       string;
	body?:      unknown;
	query?:     unknown;
}

class FakeVscode {
	posted: PostedMessage[] = [];
	postMessage(msg: unknown): void { this.posted.push(msg as PostedMessage); }
}

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_type: 'message', handler: (e: { data: unknown }) => void): void { this.handlers.push(handler); }
	fire(data: unknown): void { for (const h of this.handlers) h({ data }); }
}

function makeProject(over: Partial<ProjectEntry> = {}): ProjectEntry {
	return {
		id:                'p1',
		path:              '/repo',
		label:             'Repo',
		color:             'blue',
		added_at:          '2026-01-01T00:00:00Z',
		created_at:        '2026-01-01T00:00:00Z',
		updated_at:        '2026-01-01T00:00:00Z',
		sidebar_collapsed: false,
		sort_index:        0,
		...over,
	};
}

function reply(source: FakeMessageSource, requestId: string, status: number, body: unknown): void {
	source.fire({ type: 'http_response', request_id: requestId, status, body });
}

describe('useProjectsStore', () => {
	let vscode: FakeVscode;
	let source: FakeMessageSource;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		vscode = new FakeVscode();
		source = new FakeMessageSource();
		LisRpcClient.install(vscode, source);
		// Reset store state.
		useProjectsStore.setState({ projects: [], activeProjectId: null, loading: false, lastError: null });
	});

	test('loadAll populates projects sorted by sort_index then created_at', async () => {
		const promise = useProjectsStore.getState().loadAll();
		const env = vscode.posted[0];
		expect(env.method).toBe('GET');
		expect(env.path).toBe('/projects');

		reply(source, env.request_id, 200, [
			makeProject({ id: 'a', sort_index: 2 }),
			makeProject({ id: 'b', sort_index: 0 }),
			makeProject({ id: 'c', sort_index: 1 }),
		]);
		await promise;

		const ids = useProjectsStore.getState().projects.map(p => p.id);
		expect(ids).toEqual(['b', 'c', 'a']);
	});

	test('addProject posts the right body and applies the created project optimistically', async () => {
		const promise = useProjectsStore.getState().addProject('/repo', { label: 'Repo', color: 'purple' });
		const env = vscode.posted[0];
		expect(env.method).toBe('POST');
		expect(env.body).toEqual({ path: '/repo', label: 'Repo', color: 'purple' });

		reply(source, env.request_id, 201, makeProject({ id: 'p1', color: 'purple' }));
		const result = await promise;

		expect(result?.id).toBe('p1');
		expect(useProjectsStore.getState().projects[0].id).toBe('p1');
	});

	test('updateProjectMeta maps camelCase keys to snake_case wire keys', async () => {
		const promise = useProjectsStore.getState().updateProjectMeta('p1', {
			label:            'New',
			iconBackground:   '#fff',
			sidebarCollapsed: true,
		});
		const env = vscode.posted[0];
		expect(env.method).toBe('PATCH');
		expect(env.body).toEqual({ label: 'New', icon_background: '#fff', sidebar_collapsed: true });

		reply(source, env.request_id, 200, makeProject({ id: 'p1', label: 'New' }));
		await promise;
	});

	test('removeProject delegates to DELETE and removes locally', async () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);

		const promise = useProjectsStore.getState().removeProject('p1');
		const env = vscode.posted[0];
		expect(env.method).toBe('DELETE');
		reply(source, env.request_id, 204, null);
		await promise;

		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['p2']);
	});

	test('reorderProjects applies optimistic local order then POSTs the full id list', async () => {
		useProjectsStore.getState()._setFromServer([
			makeProject({ id: 'a', sort_index: 0 }),
			makeProject({ id: 'b', sort_index: 1 }),
			makeProject({ id: 'c', sort_index: 2 }),
		]);

		const promise = useProjectsStore.getState().reorderProjects(0, 2);
		// Optimistic update happened before the await.
		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['b', 'c', 'a']);

		const env = vscode.posted[0];
		expect(env.method).toBe('POST');
		expect(env.path).toBe('/projects/reorder');
		expect(env.body).toEqual({ ordered_external_ids: ['b', 'c', 'a'] });
		reply(source, env.request_id, 204, null);
		await promise;
	});

	test('reorderProjects with invalid indices is a no-op', async () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'a' })]);
		await useProjectsStore.getState().reorderProjects(-1, 0);
		expect(vscode.posted.length).toBe(0);
	});

	test('setActiveProject fires a touch call', async () => {
		useProjectsStore.getState().setActiveProject('p1');
		expect(useProjectsStore.getState().activeProjectId).toBe('p1');
		const env = vscode.posted[0];
		expect(env.path).toBe('/projects/p1/touch');
		expect(env.method).toBe('POST');
	});

	test('getActiveProject returns the matching project or null', () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' })]);
		useProjectsStore.getState().setActiveProjectIdOnly('p1');
		expect(useProjectsStore.getState().getActiveProject()?.id).toBe('p1');

		useProjectsStore.getState().setActiveProjectIdOnly('absent');
		expect(useProjectsStore.getState().getActiveProject()).toBeNull();
	});

	test('SSE project_created appends to local state via installProjectsSync', async () => {
		const unsubscribe = installProjectsSync();
		// Resolve the initial loadAll request with empty so the bootstrap call settles cleanly.
		reply(source, vscode.posted[0].request_id, 200, []);
		await Promise.resolve();

		source.fire({ type: 'sse_event', source: 'events', event: 'project_created', data: { project: makeProject({ id: 'sse-1' }) } });

		expect(useProjectsStore.getState().projects.map(p => p.id)).toContain('sse-1');
		unsubscribe();
	});

	test('SSE project_deleted removes from local state', () => {
		useProjectsStore.getState()._setFromServer([makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);
		const unsubscribe = installProjectsSync();
		reply(source, vscode.posted[0].request_id, 200, [makeProject({ id: 'p1' }), makeProject({ id: 'p2' })]);

		source.fire({ type: 'sse_event', source: 'events', event: 'project_deleted', data: { project_id: 'p1' } });

		expect(useProjectsStore.getState().projects.map(p => p.id)).toEqual(['p2']);
		unsubscribe();
	});

	test('uploadProjectIcon encodes file as base64 and POSTs', async () => {
		const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'icon.png', { type: 'image/png' });
		const promise = useProjectsStore.getState().uploadProjectIcon('p1', file);
		// file.arrayBuffer() is async — give it a tick before the request fires.
		while (vscode.posted.length === 0) await new Promise(r => setTimeout(r, 5));
		const env = vscode.posted[0];
		expect(env.method).toBe('POST');
		expect(env.path).toBe('/projects/p1/icon');
		expect((env.body as { mime: string; source: string }).mime).toBe('image/png');
		expect((env.body as { source: string }).source).toBe('custom');
		expect(typeof (env.body as { blob_base64: string }).blob_base64).toBe('string');

		reply(source, env.request_id, 200, makeProject({ id: 'p1', icon_image_mime: 'image/png' }));
		const result = await promise;
		expect(result.ok).toBe(true);
	});

	test('lastError is set on a failing add', async () => {
		const promise = useProjectsStore.getState().addProject('');
		reply(source, vscode.posted[0].request_id, 400, { error: 'path required' });
		const result = await promise;
		expect(result).toBeNull();
		expect(useProjectsStore.getState().lastError).toContain('path required');
	});
});
