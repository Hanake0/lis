import { beforeAll, beforeEach, describe, expect, test } from 'bun:test';

import { useUIStore } from './useUIStore';

// bun's default runtime has no `localStorage`; stub a minimal map-backed shim
// so the favorite-model tests can exercise the read/write side-effect.
beforeAll(() => {
	if (typeof globalThis.localStorage !== 'undefined') return;
	const store = new Map<string, string>();
	(globalThis as unknown as { localStorage: Storage }).localStorage = {
		get length() { return store.size; },
		clear:      () => store.clear(),
		getItem:    (k: string) => store.get(k) ?? null,
		setItem:    (k: string, v: string) => { store.set(k, v); },
		removeItem: (k: string) => { store.delete(k); },
		key:        (i: number) => Array.from(store.keys())[i] ?? null,
	};
});

/**
 * Focused tests for the actions the sidebar + dialogs actually consume from
 * useUIStore — the rest of the store's surface is documented noops for OC
 * features that don't exist in the Lis sidebar yet.
 */
describe('useUIStore', () => {
	beforeEach(() => {
		// Reset localStorage so the favorite-model tests start clean.
		try { localStorage.removeItem('lis.favoriteModels'); } catch { /* no localStorage in this env */ }
		useUIStore.setState({
			isHelpDialogOpen:              false,
			isMultiRunLauncherOpen:        false,
			multiRunLauncherPrefillPrompt: null,
			notesPanelHeight:              200,
			todoPanelHeight:               200,
			settingsPage:                  null,
			settingsProjectsSelectedId:    null,
			favoriteModels:                new Set<string>(),
		});
	});

	test('toggleHelpDialog flips isHelpDialogOpen', () => {
		expect(useUIStore.getState().isHelpDialogOpen).toBe(false);
		useUIStore.getState().toggleHelpDialog();
		expect(useUIStore.getState().isHelpDialogOpen).toBe(true);
		useUIStore.getState().toggleHelpDialog();
		expect(useUIStore.getState().isHelpDialogOpen).toBe(false);
	});

	test('openMultiRunLauncher sets isOpen=true and clears the prefill', () => {
		useUIStore.setState({ multiRunLauncherPrefillPrompt: 'old prompt' });
		useUIStore.getState().openMultiRunLauncher();
		expect(useUIStore.getState().isMultiRunLauncherOpen).toBe(true);
		expect(useUIStore.getState().multiRunLauncherPrefillPrompt).toBeNull();
	});

	test('openMultiRunLauncherWithPrompt sets the prefill', () => {
		useUIStore.getState().openMultiRunLauncherWithPrompt('repro test X');
		expect(useUIStore.getState().isMultiRunLauncherOpen).toBe(true);
		expect(useUIStore.getState().multiRunLauncherPrefillPrompt).toBe('repro test X');
	});

	test('setNotesPanelHeight + setTodoPanelHeight persist a number', () => {
		useUIStore.getState().setNotesPanelHeight(420);
		useUIStore.getState().setTodoPanelHeight(180);
		expect(useUIStore.getState().notesPanelHeight).toBe(420);
		expect(useUIStore.getState().todoPanelHeight).toBe(180);
	});

	test('setSettingsPage stores the page string (or null)', () => {
		useUIStore.getState().setSettingsPage('github');
		expect(useUIStore.getState().settingsPage).toBe('github');
		useUIStore.getState().setSettingsPage(null);
		expect(useUIStore.getState().settingsPage).toBeNull();
	});

	test('setSettingsProjectsSelectedId roundtrips', () => {
		useUIStore.getState().setSettingsProjectsSelectedId('proj-7');
		expect(useUIStore.getState().settingsProjectsSelectedId).toBe('proj-7');
	});

	test('isFavoriteModel returns false for an unfavourited model', () => {
		expect(useUIStore.getState().isFavoriteModel('any/model')).toBe(false);
	});

	test('isFavoriteModel rejects empty / non-string ids without throwing', () => {
		expect(useUIStore.getState().isFavoriteModel('')).toBe(false);
		expect((useUIStore.getState() as { isFavoriteModel: (m: unknown) => boolean }).isFavoriteModel(null)).toBe(false);
	});

	test('toggleFavoriteModel adds + removes a model and isFavoriteModel reflects it', () => {
		useUIStore.getState().toggleFavoriteModel('claude/sonnet-4');
		expect(useUIStore.getState().isFavoriteModel('claude/sonnet-4')).toBe(true);

		useUIStore.getState().toggleFavoriteModel('claude/sonnet-4');
		expect(useUIStore.getState().isFavoriteModel('claude/sonnet-4')).toBe(false);
	});

	test('toggleFavoriteModel persists across store re-reads via localStorage', () => {
		useUIStore.getState().toggleFavoriteModel('claude/sonnet-4');
		// Round-trip the persisted blob — the store reads localStorage at module init,
		// so we just assert the side-effect made it to storage.
		const raw = localStorage.getItem('lis.favoriteModels');
		expect(raw).toBeString();
		expect(JSON.parse(raw!)).toEqual(['claude/sonnet-4']);
	});

	test('toggleFavoriteModel is a noop for empty model ids', () => {
		useUIStore.getState().toggleFavoriteModel('');
		expect(useUIStore.getState().isFavoriteModel('')).toBe(false);
		expect(localStorage.getItem('lis.favoriteModels')).toBeNull();
	});
});
