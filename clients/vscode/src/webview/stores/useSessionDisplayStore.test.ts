import { beforeEach, describe, expect, test } from 'bun:test';

import { useSessionDisplayStore } from './useSessionDisplayStore';

describe('useSessionDisplayStore', () => {
	beforeEach(() => {
		useSessionDisplayStore.setState({ displayMode: 'default', showRecentSection: true });
	});

	test('defaults: displayMode=default and showRecentSection=true', () => {
		const s = useSessionDisplayStore.getState();
		expect(s.displayMode).toBe('default');
		expect(s.showRecentSection).toBe(true);
	});

	test('setDisplayMode overrides the mode', () => {
		useSessionDisplayStore.getState().setDisplayMode('minimal');
		expect(useSessionDisplayStore.getState().displayMode).toBe('minimal');
	});

	test('setShowRecentSection toggles to the explicit value', () => {
		useSessionDisplayStore.getState().setShowRecentSection(false);
		expect(useSessionDisplayStore.getState().showRecentSection).toBe(false);
	});

	test('toggleRecentSection flips boolean state', () => {
		useSessionDisplayStore.getState().toggleRecentSection();
		expect(useSessionDisplayStore.getState().showRecentSection).toBe(false);
		useSessionDisplayStore.getState().toggleRecentSection();
		expect(useSessionDisplayStore.getState().showRecentSection).toBe(true);
	});
});
