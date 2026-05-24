import { beforeEach, describe, expect, test } from 'bun:test';

import { useUIStore } from './useUIStore';

/**
 * Focused tests for the actions the sidebar + dialogs actually consume from
 * useUIStore — the rest of the store's surface is documented noops for OC
 * features that don't exist in the Lis sidebar yet.
 */
describe('useUIStore', () => {
	beforeEach(() => {
		useUIStore.setState({
			isHelpDialogOpen:              false,
			isMultiRunLauncherOpen:        false,
			multiRunLauncherPrefillPrompt: null,
			notesPanelHeight:              200,
			todoPanelHeight:               200,
			settingsPage:                  null,
			settingsProjectsSelectedId:    null,
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

	test('isFavoriteModel returns false (favorites are not wired in Lis yet)', () => {
		expect(useUIStore.getState().isFavoriteModel('any/model')).toBe(false);
	});
});
