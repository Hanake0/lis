/**
 * In-process event bus for cross-component signals that don't belong in a
 * zustand store (e.g. "project actions updated", "worktree refreshed"). OC's
 * counterpart sits on top of a custom event dispatcher; Lis uses the same
 * pattern with a Set of handlers + a typed envelope so dynamic subscribers
 * can register/unregister cheaply.
 */

export type OpenchamberEventName =
	| 'project-actions-updated'
	| 'project-notes-updated'
	| 'worktree-refreshed'
	| 'directory-changed'
	| string; // accept arbitrary string event names so consumers can opt in

export interface OpenchamberEvent {
	name:    OpenchamberEventName;
	payload?: unknown;
}

type Listener = (event: OpenchamberEvent) => void;

const listeners = new Set<Listener>();

/** Broadcast an event to every subscriber. Errors in one handler don't block the others. */
export function emitOpenchamberEvent(name: OpenchamberEventName, payload?: unknown): void {
	const event: OpenchamberEvent = { name, payload };
	for (const listener of listeners) {
		try { listener(event); }
		catch (err) { console.warn('[openchamberEvents] handler threw:', name, err); }
	}
}

/**
 * Subscribe to every event. Returns the unsubscribe function — components
 * should call it in their effect cleanup. Idempotent: registering the same
 * handler twice still results in a single subscription.
 */
export function subscribeOpenchamberEvents(handler: Listener): () => void {
	listeners.add(handler);
	return () => { listeners.delete(handler); };
}

/** Test-only — clear every subscriber. */
export function _resetOpenchamberEventsForTesting(): void {
	listeners.clear();
}
