import { create } from 'zustand';

import {
	type ActiveNowEntry,
	addActiveNowSession,
	persistActiveNowEntries,
	pruneActiveNowEntries,
	readActiveNowEntries,
} from '@/components/session/sidebar/activitySections';
import type { Session } from '@opencode-ai/sdk/v2';
import { getSafeStorage } from './utils/safeStorage';

/**
 * "Active now" — a sticky most-recently-touched-by-the-user list that surfaces
 * in the sidebar's pinned section even when SessionStatus has gone cold. The
 * Lis variant matches OC byte-for-byte except that `getSafeStorage()` here
 * returns `Storage | null` (the OC equivalent does too — kept just for safety).
 *
 * Helpers in [activitySections.ts](../components/session/sidebar/activitySections.ts)
 * own all the parsing + pruning logic; this store is the zustand shell.
 */

interface ActiveNowStore {
	entries:    ActiveNowEntry[];
	setEntries: (entries: ActiveNowEntry[]) => void;
	addSession: (sessionId: string) => void;
	prune:      (sessionsById: Map<string, Session>) => void;
}

/** Storage may be null (e.g. tests, sandboxed contexts). All callers must tolerate that. */
const noopStorage: Storage = {
	length:        0,
	clear:         () => { /* noop */ },
	getItem:       () => null,
	key:           () => null,
	removeItem:    () => { /* noop */ },
	setItem:       () => { /* noop */ },
};

const storage: Storage = getSafeStorage() ?? noopStorage;

export const useActiveNowStore = create<ActiveNowStore>((set, get) => ({
	entries: readActiveNowEntries(storage),

	setEntries: (entries) => {
		if (entries === get().entries) return;
		set({ entries });
		persistActiveNowEntries(storage, entries);
	},

	addSession: (sessionId) => {
		const next = addActiveNowSession(get().entries, sessionId);
		if (next === get().entries) return;
		set({ entries: next });
		persistActiveNowEntries(storage, next);
	},

	prune: (sessionsById) => {
		const current = get().entries;
		const pruned = pruneActiveNowEntries(current, sessionsById);
		// Identity-stable optimisation: same length + same ids → skip the set + storage write.
		if (
			pruned.length === current.length
			&& pruned.every((entry, index) => entry.sessionId === current[index]?.sessionId)
		) return;
		set({ entries: pruned });
		persistActiveNowEntries(storage, pruned);
	},
}));
