import { beforeEach, describe, expect, test } from 'bun:test';

import { LisRpcClient, LisRpcError, type MessageSource } from './rpc-client';

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
	postMessage(msg: unknown): void {
		this.posted.push(msg as PostedMessage);
	}
}

class FakeMessageSource implements MessageSource {
	private handlers: ((e: { data: unknown }) => void)[] = [];
	addEventListener(_type: 'message', handler: (e: { data: unknown }) => void): void {
		this.handlers.push(handler);
	}
	fire(data: unknown): void {
		for (const h of this.handlers) h({ data });
	}
}

let source: FakeMessageSource;

function reply(requestId: string, status: number, body: unknown): void {
	source.fire({ type: 'http_response', request_id: requestId, status, body });
}

function fireSse(event: string, data: unknown, src: 'events' | 'chat' = 'events', chatId?: string): void {
	source.fire({ type: 'sse_event', source: src, event, data, chat_id: chatId });
}

function fireConnectionState(state: 'connecting' | 'connected' | 'disconnected'): void {
	source.fire({ type: 'connection_state', events: state });
}

describe('LisRpcClient', () => {
	let vscode: FakeVscode;

	beforeEach(() => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		vscode = new FakeVscode();
		source = new FakeMessageSource();
		LisRpcClient.install(vscode, source);
	});

	test('listProjects builds the right envelope and resolves with the response body', async () => {
		const client = LisRpcClient.instance();
		const promise = client.listProjects();

		expect(vscode.posted.length).toBe(1);
		const env = vscode.posted[0];
		expect(env.type).toBe('http_request');
		expect(env.method).toBe('GET');
		expect(env.path).toBe('/projects');

		reply(env.request_id, 200, [{ id: 'p1' }]);

		const result = await promise as { id: string }[];
		expect(result[0].id).toBe('p1');
	});

	test('createProject posts a JSON body', async () => {
		const client = LisRpcClient.instance();
		const promise = client.createProject({ path: '/repo', label: 'Repo' });

		const env = vscode.posted[0];
		expect(env.method).toBe('POST');
		expect(env.path).toBe('/projects');
		expect(env.body).toEqual({ path: '/repo', label: 'Repo' });

		reply(env.request_id, 201, { id: 'abc' });
		const result = await promise as { id: string };
		expect(result.id).toBe('abc');
	});

	test('rejects with LisRpcError on non-2xx status', async () => {
		const client = LisRpcClient.instance();
		const promise = client.getProject('missing');

		reply(vscode.posted[0].request_id, 404, { error: 'not found' });

		await expect(promise).rejects.toBeInstanceOf(LisRpcError);
		try { await promise; }
		catch (err) {
			expect((err as LisRpcError).status).toBe(404);
		}
	});

	test('encodes URL-unsafe ids in path segments', async () => {
		const client = LisRpcClient.instance();
		void client.getProject('weird/id?x=1');
		expect(vscode.posted[0].path).toBe('/projects/weird%2Fid%3Fx%3D1');
	});

	test('passes query params for listSessionFolders', async () => {
		const client = LisRpcClient.instance();
		void client.listSessionFolders('dir:/a');
		expect(vscode.posted[0].query).toEqual({ scope: 'dir:/a' });
	});

	test('removeWorktree sends a DELETE with a JSON body', async () => {
		const client = LisRpcClient.instance();
		void client.removeWorktree('/proj', '/wt', true);
		const env = vscode.posted[0];
		expect(env.method).toBe('DELETE');
		expect(env.path).toBe('/worktrees');
		expect(env.body).toEqual({ project_path: '/proj', worktree_path: '/wt', force: true });
	});

	test('onSse fans out events from the host', async () => {
		const client = LisRpcClient.instance();
		const received: { event: string; data: unknown; source: string }[] = [];
		client.onSse((event, data, meta) => received.push({ event, data, source: meta.source }));

		fireSse('project_created', { id: 'p1' });
		fireSse('chunk',           { text: 'hi' }, 'chat', 'chat-1');

		expect(received.length).toBe(2);
		expect(received[0].event).toBe('project_created');
		expect(received[1].source).toBe('chat');
	});

	test('onState fans out connection-state transitions', () => {
		const client = LisRpcClient.instance();
		const states: string[] = [];
		client.onState((s) => states.push(s));

		fireConnectionState('connecting');
		fireConnectionState('connected');
		fireConnectionState('disconnected');

		expect(states).toEqual(['connecting', 'connected', 'disconnected']);
	});

	test('moveChat to project posts the right body', async () => {
		const client = LisRpcClient.instance();
		void client.moveChat('chat-1', 'proj-x');
		expect(vscode.posted[0].path).toBe('/chats/chat-1/move');
		expect(vscode.posted[0].body).toEqual({ project_external_id: 'proj-x' });
	});

	test('moveChat detach sends null project id', async () => {
		const client = LisRpcClient.instance();
		void client.moveChat('chat-1', null);
		expect(vscode.posted[0].body).toEqual({ project_external_id: null });
	});

	test('install is idempotent', () => {
		const a = LisRpcClient.install(vscode, source);
		const b = LisRpcClient.install(vscode, source);
		expect(a).toBe(b);
	});

	test('instance throws before install', () => {
		(LisRpcClient as unknown as { singleton: LisRpcClient | null }).singleton = null;
		expect(() => LisRpcClient.instance()).toThrow();
	});
});
