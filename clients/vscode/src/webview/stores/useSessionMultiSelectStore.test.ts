import { beforeEach, describe, expect, test } from 'bun:test';

import { useSessionMultiSelectStore } from './useSessionMultiSelectStore';

describe('useSessionMultiSelectStore', () => {
	beforeEach(() => {
		useSessionMultiSelectStore.setState({
			enabled:     false,
			selectedIds: new Set<string>(),
			scopeKey:    null,
			anchorId:    null,
		});
	});

	test('enable flips selection mode on; idempotent on re-enable', () => {
		useSessionMultiSelectStore.getState().enable();
		expect(useSessionMultiSelectStore.getState().enabled).toBe(true);
		useSessionMultiSelectStore.getState().enable();
		expect(useSessionMultiSelectStore.getState().enabled).toBe(true);
	});

	test('disable clears all selection state', () => {
		useSessionMultiSelectStore.setState({ enabled: true, selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().disable();
		const s = useSessionMultiSelectStore.getState();
		expect(s.enabled).toBe(false);
		expect(s.selectedIds.size).toBe(0);
		expect(s.scopeKey).toBeNull();
		expect(s.anchorId).toBeNull();
	});

	test('toggleMode flips between off and on, clearing on off', () => {
		useSessionMultiSelectStore.getState().toggleMode();
		expect(useSessionMultiSelectStore.getState().enabled).toBe(true);
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['x']), scopeKey: 'dir:/x', anchorId: 'x' });
		useSessionMultiSelectStore.getState().toggleMode();
		const s = useSessionMultiSelectStore.getState();
		expect(s.enabled).toBe(false);
		expect(s.selectedIds.size).toBe(0);
		expect(s.anchorId).toBeNull();
	});

	test('toggleSelected adds a previously-unselected id and sets the anchor', () => {
		useSessionMultiSelectStore.getState().toggleSelected('a', 'dir:/repo');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['a']);
		expect(s.anchorId).toBe('a');
		expect(s.scopeKey).toBe('dir:/repo');
	});

	test('toggleSelected removes an already-selected id; resets anchor when it matches', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a', 'b']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().toggleSelected('a', 'dir:/x');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['b']);
		expect(s.anchorId).toBeNull();
	});

	test('toggleSelected with descendants toggles them as a unit', () => {
		useSessionMultiSelectStore.getState().toggleSelected('p', 'dir:/x', ['c1', 'c2']);
		expect(Array.from(useSessionMultiSelectStore.getState().selectedIds).sort()).toEqual(['c1', 'c2', 'p']);

		useSessionMultiSelectStore.getState().toggleSelected('p', 'dir:/x', ['c1', 'c2']);
		expect(useSessionMultiSelectStore.getState().selectedIds.size).toBe(0);
	});

	test('toggleSelected changing scope discards previous selections', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().toggleSelected('y', 'dir:/other');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['y']);
		expect(s.scopeKey).toBe('dir:/other');
		expect(s.anchorId).toBe('y');
	});

	test('toggleSelected with no anchor scope inherits the incoming scope', () => {
		useSessionMultiSelectStore.getState().toggleSelected('z', 'dir:/scope');
		expect(useSessionMultiSelectStore.getState().scopeKey).toBe('dir:/scope');
	});

	test('setRange selects an inclusive forward range and remembers the anchor', () => {
		useSessionMultiSelectStore.getState().setRange(null, 'c', ['a', 'b', 'c', 'd'], 'dir:/x');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds).sort()).toEqual(['a', 'b', 'c']);
		expect(s.anchorId).toBe('a');
	});

	test('setRange supports a reverse selection from anchor', () => {
		useSessionMultiSelectStore.getState().setRange('d', 'b', ['a', 'b', 'c', 'd'], 'dir:/x');
		expect(Array.from(useSessionMultiSelectStore.getState().selectedIds).sort()).toEqual(['b', 'c', 'd']);
	});

	test('setRange expands descendants for each in-range id', () => {
		const descendants = new Map<string, string[]>([['b', ['b1', 'b2']]]);
		useSessionMultiSelectStore.getState().setRange('a', 'c', ['a', 'b', 'c'], 'dir:/x', descendants);
		expect(Array.from(useSessionMultiSelectStore.getState().selectedIds).sort()).toEqual(['a', 'b', 'b1', 'b2', 'c']);
	});

	test('setRange across scopes drops the previous selection', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['x']), scopeKey: 'dir:/other', anchorId: 'x' });
		useSessionMultiSelectStore.getState().setRange(null, 'b', ['a', 'b'], 'dir:/x');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds).sort()).toEqual(['a', 'b']);
		expect(s.scopeKey).toBe('dir:/x');
	});

	test('setRange on empty list is a no-op', () => {
		useSessionMultiSelectStore.getState().setRange(null, 'a', [], 'dir:/x');
		expect(useSessionMultiSelectStore.getState().selectedIds.size).toBe(0);
	});

	test('replaceAll replaces ids and anchors on the first', () => {
		useSessionMultiSelectStore.getState().replaceAll(['1', '2', '3'], 'dir:/x');
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['1', '2', '3']);
		expect(s.anchorId).toBe('1');
		expect(s.scopeKey).toBe('dir:/x');
	});

	test('replaceAll with an empty list clears everything', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().replaceAll([], null);
		const s = useSessionMultiSelectStore.getState();
		expect(s.selectedIds.size).toBe(0);
		expect(s.scopeKey).toBeNull();
		expect(s.anchorId).toBeNull();
	});

	test('clear empties the selection', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().clear();
		expect(useSessionMultiSelectStore.getState().selectedIds.size).toBe(0);
	});

	test('removeMany strips a subset and keeps scope when some remain', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a', 'b', 'c']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().removeMany(['a', 'b']);
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['c']);
		expect(s.scopeKey).toBe('dir:/x');
	});

	test('removeMany clearing the last id resets scope and anchor', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().removeMany(['a']);
		const s = useSessionMultiSelectStore.getState();
		expect(s.selectedIds.size).toBe(0);
		expect(s.scopeKey).toBeNull();
		expect(s.anchorId).toBeNull();
	});

	test('removeMany with no overlap is a no-op (no state change)', () => {
		useSessionMultiSelectStore.setState({ selectedIds: new Set(['a']), scopeKey: 'dir:/x', anchorId: 'a' });
		useSessionMultiSelectStore.getState().removeMany(['z']);
		const s = useSessionMultiSelectStore.getState();
		expect(Array.from(s.selectedIds)).toEqual(['a']);
	});
});
