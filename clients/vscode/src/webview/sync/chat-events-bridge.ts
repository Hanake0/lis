/**
 * Bridges the global SSE channel (`/api/events`) to the OC-shaped
 * useGlobalSessionsStore. Server-side chat lifecycle events
 * (chat_created/updated/deleted/archived/moved) are translated into the
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

import type { Session } from '@/lib/opencode-sdk-stub';
import { LisRpcClient } from '@/lib/lis/rpc-client';
import type { LisChatSummary } from '@/lib/lis/rpc-client';
import {
	chatSummaryToSession,
	useGlobalSessionsStore,
} from '@/stores/useGlobalSessionsStore';
import { useProjectsStore } from '@/stores/useProjectsStore';
import { normalizePath } from '@/components/session/sidebar/utils';

/** Wire payload of `chat_created`/`chat_updated`/`chat_archived`. */
interface ChatPayloadWithDto { chat: LisChatSummary }
/** Wire payload of `chat_deleted`. */
interface ChatDeletedPayload { chat_id: string }
/** Wire payload of `chat_moved` — does NOT include the full chat DTO. */
interface ChatMovedPayload { chat_id: string; project_id: string | null }

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

function isChatMovedPayload(data: unknown): data is ChatMovedPayload {
	if (typeof data !== 'object' || data === null) return false;
	const d = data as { chat_id?: unknown; project_id?: unknown };
	if (typeof d.chat_id !== 'string') return false;
	// Backend's JsonOpt has DefaultIgnoreCondition=WhenWritingNull so a chat
	// detached to no-project arrives as `{ chat_id }` with `project_id` absent.
	// Treat missing AS null. Reject only invalid (non-string-non-null) values.
	return d.project_id === undefined || d.project_id === null || typeof d.project_id === 'string';
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
			case 'chat_moved': {
				// Payload is a thin {chat_id, project_id} — no full DTO. Find the
				// existing session in the store, clone it with the new projectId
				// (and the new resolved `directory` from the project's path), and
				// upsert. The dedup signature includes the resolved directory, so
				// the re-bucket happens automatically once the dir changes.
				//
				// project_id is intentionally JSON-omitted by the backend when
				// the chat is detached (WhenWritingNull policy) — normalize a
				// missing field to null so the detach path works.
				if (!isChatMovedPayload(ev.data)) return;
				const chat_id    = ev.data.chat_id;
				const project_id = ev.data.project_id ?? null;
				const store    = useGlobalSessionsStore.getState();
				const existing = store.activeSessions.find(s => s.id === chat_id)
					?? store.archivedSessions.find(s => s.id === chat_id)
					?? null;
				if (!existing) return; // unknown chat — next loadSessions() will pick it up
				const projectPath = project_id
					? normalizePath(useProjectsStore.getState().projects.find(p => p.id === project_id)?.path ?? null)
					: null;
				const next: Session = {
					...existing,
					directory: projectPath,
					projectId: project_id,
				} as Session;
				store.upsertSession(next);
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
