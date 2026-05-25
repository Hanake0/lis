/**
 * Bridges the global SSE channel (`/api/events`) to the OC-shaped
 * useGlobalSessionsStore. Server-side chat lifecycle events
 * (chat_created/updated/deleted/archived) are translated into the
 * store's CRUD primitives so the sidebar's session list stays
 * in sync without any explicit refresh.
 *
 * Mount once near app boot — `mountChatEventsBridge()` registers the
 * subscription via `LisRpcClient.instance().onSse(...)` and returns
 * an unsubscribe function for cleanup (HMR, sign-out, etc.).
 *
 * Why a bridge module rather than subscribing inside the store: the
 * store stays pure (CRUD primitives, signature-dedup, scope map).
 * Wiring lives at the seam between transport (SSE) and state
 * (Zustand) — matches OC's `sync-context` pattern where event-driven
 * mutations happen in one focused place.
 */

import { LisRpcClient } from '@/lib/lis/rpc-client';
import type { LisChatSummary } from '@/lib/lis/rpc-client';
import {
	chatSummaryToSession,
	useGlobalSessionsStore,
} from '@/stores/useGlobalSessionsStore';

/** Wire payload of `chat_created`/`chat_updated`/`chat_archived`. */
interface ChatPayloadWithDto { chat: LisChatSummary }
/** Wire payload of `chat_deleted`. */
interface ChatDeletedPayload { chat_id: string }

function isChatPayload(data: unknown): data is ChatPayloadWithDto {
	if (typeof data !== 'object' || data === null) return false;
	const d = data as { chat?: unknown };
	if (typeof d.chat !== 'object' || d.chat === null) return false;
	return typeof (d.chat as { chat_id?: unknown }).chat_id === 'string';
}

function isChatDeletedPayload(data: unknown): data is ChatDeletedPayload {
	return typeof data === 'object'
		&& data !== null
		&& typeof (data as { chat_id?: unknown }).chat_id === 'string';
}

/**
 * Subscribe to chat-lifecycle SSE events and route them into useGlobalSessionsStore.
 * Returns an unsubscribe function; calling it stops the bridge but does NOT close
 * the underlying SSE connection (that's owned by LisRpcClient).
 */
export function mountChatEventsBridge(): () => void {
	const client = LisRpcClient.instance();
	return client.onSse((ev) => {
		switch (ev.event) {
			case 'chat_created':
			case 'chat_updated':
			case 'chat_archived': {
				if (!isChatPayload(ev.data)) return;
				useGlobalSessionsStore.getState().upsertSession(chatSummaryToSession(ev.data.chat));
				return;
			}
			case 'chat_deleted': {
				if (!isChatDeletedPayload(ev.data)) return;
				useGlobalSessionsStore.getState().removeSessions([ev.data.chat_id]);
				return;
			}
			default:
				return;
		}
	});
}
