import { create } from 'zustand';
import { persist, type PersistStorage } from 'zustand/middleware';

import { getSafeStorage } from './utils/safeStorage';

/**
 * Sidebar display preferences. Persisted to `localStorage` so the user's chosen
 * density + "show recent" toggle survive webview reloads. Ported from OC
 * `useSessionDisplayStore` — kept tiny because every property is pure UI state.
 */

export type SessionDisplayMode = 'default' | 'minimal';

interface SessionDisplayState {
	displayMode:           SessionDisplayMode;
	showRecentSection:     boolean;
	setDisplayMode:        (mode: SessionDisplayMode) => void;
	setShowRecentSection:  (show: boolean) => void;
	toggleRecentSection:   () => void;
}

/** Adapter from `Storage | null` (our `getSafeStorage`) to the JSON-shaped Zustand persist storage. */
function makePersistStorage(): PersistStorage<{ displayMode: SessionDisplayMode; showRecentSection: boolean }> | undefined {
	const storage = getSafeStorage();
	if (!storage) return undefined;
	return {
		getItem: (name) => {
			const raw = storage.getItem(name);
			return raw ? JSON.parse(raw) : null;
		},
		setItem: (name, value) => storage.setItem(name, JSON.stringify(value)),
		removeItem: (name) => storage.removeItem(name),
	};
}

export const useSessionDisplayStore = create<SessionDisplayState>()(
	persist(
		(set) => ({
			displayMode:          'default',
			showRecentSection:    true,
			setDisplayMode:       (mode) => set({ displayMode: mode }),
			setShowRecentSection: (show) => set({ showRecentSection: show }),
			toggleRecentSection:  () => set((s) => ({ showRecentSection: !s.showRecentSection })),
		}),
		{
			name:    'session-display-mode',
			storage: makePersistStorage(),
			// Only persist serialisable preferences — actions are reconstructed on rehydrate.
			partialize: (state) => ({ displayMode: state.displayMode, showRecentSection: state.showRecentSection }),
		},
	),
);
