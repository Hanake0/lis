import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import { LisEventStream, type SseEvent } from './lisEventStream';

/**
 * Build a ReadableStream that emits the given chunks one at a time. Each chunk
 * is sent as UTF-8. Returns the stream — the caller is responsible for awaiting
 * read completion via the consumer.
 */
function streamOf(chunks: string[]): ReadableStream<Uint8Array> {
	const encoder = new TextEncoder();
	let i = 0;
	return new ReadableStream<Uint8Array>({
		pull(controller) {
			if (i >= chunks.length) {
				controller.close();
				return;
			}
			controller.enqueue(encoder.encode(chunks[i++]));
		},
	});
}

function waitFor(predicate: () => boolean, timeoutMs = 2000): Promise<void> {
	return new Promise((resolve, reject) => {
		const start = Date.now();
		const tick = () => {
			if (predicate()) return resolve();
			if (Date.now() - start > timeoutMs) return reject(new Error('timeout waiting for predicate'));
			setTimeout(tick, 10);
		};
		tick();
	});
}

describe('LisEventStream', () => {
	const originalFetch = globalThis.fetch;

	beforeEach(() => {
		// reset fetch
	});

	afterEach(() => {
		globalThis.fetch = originalFetch;
	});

	test('parses a single SSE frame with id+event+data', async () => {
		globalThis.fetch = async (): Promise<Response> => new Response(
			streamOf([
				'id: 1\n',
				'event: project_created\n',
				'data: {"id":"abc"}\n',
				'\n',
			]),
			{ status: 200, headers: { 'content-type': 'text/event-stream' } },
		);

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		const received: SseEvent[] = [];
		stream.onEvent((ev) => received.push(ev));
		stream.start();

		await waitFor(() => received.length >= 1);
		stream.stop();

		expect(received[0].id).toBe('1');
		expect(received[0].event).toBe('project_created');
		expect((received[0].data as { id: string }).id).toBe('abc');
	});

	test('parses multiple frames split across chunks', async () => {
		globalThis.fetch = async (): Promise<Response> => new Response(
			streamOf([
				'id: 1\nevent: a\ndata: {"x":1}\n\nid: 2\nevent: b\ndata',  // split mid-frame
				': {"y":2}\n\n',
				'id: 3\nevent: c\ndata: {"z":3}\n\n',
			]),
			{ status: 200, headers: { 'content-type': 'text/event-stream' } },
		);

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		const received: SseEvent[] = [];
		stream.onEvent((ev) => received.push(ev));
		stream.start();

		await waitFor(() => received.length >= 3);
		stream.stop();

		expect(received.map(e => e.event)).toEqual(['a', 'b', 'c']);
		expect(received[1].id).toBe('2');
	});

	test('updates Last-Event-ID and transitions state', async () => {
		const observedStates: string[] = [];
		globalThis.fetch = async (): Promise<Response> => new Response(
			streamOf(['id: 42\nevent: x\ndata: null\n\n']),
			{ status: 200, headers: { 'content-type': 'text/event-stream' } },
		);

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		stream.onState((s) => observedStates.push(s));

		const received: SseEvent[] = [];
		stream.onEvent((ev) => received.push(ev));

		stream.start();
		await waitFor(() => received.length >= 1);
		stream.stop();

		expect(received[0].id).toBe('42');
		// state sequence: initial 'disconnected' (registered listener fires immediately) → connecting → connected → disconnected (on stop)
		expect(observedStates).toContain('connecting');
		expect(observedStates).toContain('connected');
		expect(observedStates[observedStates.length - 1]).toBe('disconnected');
	});

	test('ignores comment lines (starting with :)', async () => {
		globalThis.fetch = async (): Promise<Response> => new Response(
			streamOf([
				': heartbeat\n',
				'event: real\ndata: null\n\n',
			]),
			{ status: 200, headers: { 'content-type': 'text/event-stream' } },
		);

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		const received: SseEvent[] = [];
		stream.onEvent((ev) => received.push(ev));
		stream.start();
		await waitFor(() => received.length >= 1);
		stream.stop();

		expect(received[0].event).toBe('real');
	});

	test('leaves data as raw string when JSON parse fails', async () => {
		globalThis.fetch = async (): Promise<Response> => new Response(
			streamOf(['event: weird\ndata: not-json\n\n']),
			{ status: 200, headers: { 'content-type': 'text/event-stream' } },
		);

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		const received: SseEvent[] = [];
		stream.onEvent((ev) => received.push(ev));
		stream.start();
		await waitFor(() => received.length >= 1);
		stream.stop();

		expect(received[0].data).toBe('not-json');
	});

	test('stop() prevents further reconnect attempts', async () => {
		let attempts = 0;
		globalThis.fetch = async (): Promise<Response> => {
			attempts++;
			throw new Error('refused');
		};

		const stream = new LisEventStream('http://localhost:3010', 'tok');
		stream.start();
		await waitFor(() => attempts >= 1);
		stream.stop();

		const snapshot = attempts;
		await new Promise(resolve => setTimeout(resolve, 50));
		expect(attempts).toBe(snapshot);
	});

	test('does not start fetching when baseOrigin is empty', async () => {
		let attempts = 0;
		globalThis.fetch = async (): Promise<Response> => {
			attempts++;
			return new Response('', { status: 200 });
		};

		const stream = new LisEventStream('', '');
		stream.start();
		await new Promise(resolve => setTimeout(resolve, 50));
		stream.stop();

		expect(attempts).toBe(0);
		expect(stream.currentState).toBe('disconnected');
	});
});
