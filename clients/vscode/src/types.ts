// Shared type definitions for the Lis extension and webview.
//
// The WS-frame unions that used to live here (ServerFrame / ClientFrame /
// ClientImageAttachment / DispatchMode / MessageRow) were removed when the
// WebSocket transport was deleted in Stage 13 — every client→server call
// now goes through REST (`Lis.Api/Controllers/*`) and every server→client
// stream through SSE (`/api/events` + `/api/chats/{id}/stream`). DTO shapes
// for those live next to the consumers:
//
//   - REST: `webview/lib/lis/rpc-client.ts` (LisChatSummary, LisMessageSummary, ...)
//   - SSE chat-stream payloads: `webview/sync/chat-panel-bridge.ts` (private)
//   - SSE global event payloads: `Lis.Api/Schemas/Events/*` mirrored ad-hoc
//
// What survives here are the shared shapes both the extension host AND the
// webview still need.

/**
 * Connection state surfaced in the VS Code status bar. With the WS layer gone,
 * the host no longer tracks live socket transitions — it derives the value
 * from `lis.serverUrl` + `lis.authToken` being set (or not). The third value
 * (`connecting`) is retained for forward-compat with a future health-probe
 * status feed without forcing a type change downstream.
 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected';

// ── Chat-store DTOs ────────────────────────────────────────────────────────
// These pre-date the LisRpcClient wire types and are kept because the legacy
// `useChatStore` (the chat-panel side of the webview) still uses them as its
// internal shape. The chat-panel bridge maps `LisChatSummary` →
// `ChatSummary` and `LisMessageSummary` → ChatMessage on the way in.

export interface ChatSummary {
	chat_id:     string;
	name?:       string;
	updated_at:  string;
	archived_at?: string;
}

export interface AgentSummary {
	name:          string;
	display_name?: string;
	model:         string;
	effort?:       string;
	tool_profile?: string;
	is_default:    boolean;
}

export interface ContextSegment {
	name:   string;
	tokens: number;
}
