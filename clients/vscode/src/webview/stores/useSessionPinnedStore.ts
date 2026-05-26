import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native session-pin store. Pins are persisted on the backend via
 * `POST/DELETE /api/chats/{id}/pin`; cross-client updates arrive via the
 * `chat_pinned` / `chat_unpinned` SSE events.
 *
 * Mirrors OC's `useSessionPinnedStore` surface (`ids`, `setIds`, `toggle`)
 * so existing components keep compiling; replaces OC's localStorage-only
 * model with a server round-trip + SSE refresh.
 */

interface SessionPinnedStore {
	ids:        Set<string>;
	lastError:  string | null;

	setIds:     (next: Set<string> | ((prev: Set<string>) => Set<string>)) => void;
	toggle:     (chatId: string) => Promise<void>;
	pin:        (chatId: string) => Promise<void>;
	unpin:      (chatId: string) => Promise<void>;
	isPinned:   (chatId: string) => boolean;

	/** Internal SSE hooks. */
	_applyPinned:   (chatId: string) => void;
	_applyUnpinned: (chatId: string) => void;
}

export const useSessionPinnedStore = create<SessionPinnedStore>((set, get) => ({
	ids:       new Set<string>(),
	lastError: null,

	setIds: (next) => {
		const current = get().ids;
		const resolved = typeof next === 'function' ? next(current) : next;
		if (resolved === current) return;
		set({ ids: resolved });
	},

	toggle: async (chatId) => {
		if (get().ids.has(chatId)) await get().unpin(chatId);
		else                       await get().pin(chatId);
	},

	pin: async (chatId) => {
		// Optimistic: flip locally first, rollback on failure.
		get()._applyPinned(chatId);
		try {
			await LisRpcClient.instance().pinChat(chatId);
		} catch (err) {
			get()._applyUnpinned(chatId);
			set({ lastError: errorMessage(err) });
		}
	},

	unpin: async (chatId) => {
		get()._applyUnpinned(chatId);
		try {
			await LisRpcClient.instance().unpinChat(chatId);
		} catch (err) {
			get()._applyPinned(chatId);
			set({ lastError: errorMessage(err) });
		}
	},

	isPinned: (chatId) => get().ids.has(chatId),

	_applyPinned: (chatId) => {
		const next = new Set(get().ids);
		next.add(chatId);
		set({ ids: next });
	},

	_applyUnpinned: (chatId) => {
		if (!get().ids.has(chatId)) return;
		const next = new Set(get().ids);
		next.delete(chatId);
		set({ ids: next });
	},
}));

/** Subscribe to chat-pin SSE events. Call once after `LisRpcClient.install`. */
export function installSessionPinnedSync(): () => void {
	const client = LisRpcClient.instance();
	const store = useSessionPinnedStore.getState();

	return client.onSse((ev) => {
		switch (ev.event) {
			case 'chat_pinned': {
				const data = ev.data as { chat_id?: string } | null;
				if (data?.chat_id) store._applyPinned(data.chat_id);
				return;
			}
			case 'chat_unpinned': {
				const data = ev.data as { chat_id?: string } | null;
				if (data?.chat_id) store._applyUnpinned(data.chat_id);
				return;
			}
			default:
				return;
		}
	});
}

function errorMessage(err: unknown): string {
	if (err instanceof LisRpcError) return err.message;
	if (err instanceof Error)        return err.message;
	return String(err);
}

// Expose for the Playwright e2e harness — same pattern as __lis_folders,
// __lis_globalSessions, __lis_config.
if (typeof window !== 'undefined') {
	(window as unknown as { __lis_pinned?: unknown }).__lis_pinned = useSessionPinnedStore;
}
