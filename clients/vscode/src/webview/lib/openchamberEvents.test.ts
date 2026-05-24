import { beforeEach, describe, expect, test } from 'bun:test';

import {
	_resetOpenchamberEventsForTesting,
	emitOpenchamberEvent,
	subscribeOpenchamberEvents,
	type OpenchamberEvent,
} from './openchamberEvents';

describe('openchamberEvents', () => {
	beforeEach(() => { _resetOpenchamberEventsForTesting(); });

	test('subscribeOpenchamberEvents receives every emitted event', () => {
		const received: OpenchamberEvent[] = [];
		subscribeOpenchamberEvents((e) => received.push(e));
		emitOpenchamberEvent('project-actions-updated', { count: 3 });
		emitOpenchamberEvent('directory-changed', { path: '/x' });
		expect(received).toEqual([
			{ name: 'project-actions-updated', payload: { count: 3 } },
			{ name: 'directory-changed',       payload: { path: '/x' } },
		]);
	});

	test('unsubscribe stops further delivery', () => {
		const received: OpenchamberEvent[] = [];
		const off = subscribeOpenchamberEvents((e) => received.push(e));
		emitOpenchamberEvent('a');
		off();
		emitOpenchamberEvent('b');
		expect(received.map(e => e.name)).toEqual(['a']);
	});

	test('handler throwing does not block subsequent handlers', () => {
		const calls: string[] = [];
		subscribeOpenchamberEvents(() => { calls.push('first'); throw new Error('boom'); });
		subscribeOpenchamberEvents(() => { calls.push('second'); });
		emitOpenchamberEvent('x');
		expect(calls).toEqual(['first', 'second']);
	});

	test('re-registering the same listener does not fire twice', () => {
		let count = 0;
		const handler = () => { count++; };
		subscribeOpenchamberEvents(handler);
		subscribeOpenchamberEvents(handler);
		emitOpenchamberEvent('x');
		expect(count).toBe(1);
	});

	test('emit without subscribers is a noop', () => {
		expect(() => emitOpenchamberEvent('orphan')).not.toThrow();
	});
});
