import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app';
import { vscode, installInboundMessageTracer } from './vscodeApi';
import { LisRpcClient } from './lib/lis/rpc-client';
import { lisLog } from './lib/lis/logger';
import { mountChatEventsBridge } from './sync/chat-events-bridge';
import { useGlobalSessionsStore } from './stores/useGlobalSessionsStore';
import { installProjectsSync } from './stores/useProjectsStore';
import { installSessionFoldersSync } from './stores/useSessionFoldersStore';
import { installSessionPinnedSync, useSessionPinnedStore } from './stores/useSessionPinnedStore';
import { installChatPanelInit } from './sync/chat-panel-bridge';

// Trace every inbound postMessage when verbose logging is on. Wire this BEFORE
// the RPC client so the trace fires for `lis.init` too.
installInboundMessageTracer();
lisLog.info('webview: boot');

// Install the RPC client BEFORE any store boots. `install()` registers the
// window message listener that translates the extension host's `lis.init` /
// `lis.config_update` postMessages into HTTP-client + SSE-channel config. The
// host sends `lis.init` in response to our `lis.ready` ping. Every subsequent
// LisRpcClient.instance() call hits the configured client.
const rpc = LisRpcClient.install(window, { vscode });

// Bridge global SSE chat-lifecycle events into useGlobalSessionsStore. Sidebar
// chat list stays in sync on create/update/delete/archive without an explicit
// refresh — the store mutations run as the events arrive on /api/events.
mountChatEventsBridge();

// Wire useProjectsStore: initial REST load + live SSE updates for
// project_created/updated/deleted.
installProjectsSync();

// Wire useSessionFoldersStore: subscribes to folder_created / folder_updated /
// folder_deleted SSE events. Without this the sidebar never reflects folder
// mutations from other clients (or from this client after an HMR boundary).
// Initial REST load per-scope happens lazily inside the sidebar's
// useFolderScopeBootstrap.
installSessionFoldersSync();

// Wire useSessionPinnedStore: subscribes to chat_pinned / chat_unpinned SSE
// events. Pins are server-side now (not localStorage), so the store starts
// empty and gets seeded both from the initial loadSessions REST response
// (below, after rpc.ready()) and from live SSE events thereafter.
installSessionPinnedSync();

// Wire useConfigStore: initial /api/providers load on rpc ready and on every
// config refresh. The store's own module-bottom bootstrap relied on
// useConfigStore being imported BEFORE the rpc.ready() resolved, which only
// happens for the sidebar (its components transitively touch useConfigStore
// early). The chat panel imports useConfigStore via ChatInput, which mounts
// AFTER rpc.ready() — by then the `.ready().then()` is a no-op race. Wiring
// here matches `installProjectsSync` so providers always load on this side
// regardless of import order.
void import('./stores/useConfigStore').then(({ useConfigStore }) => {
	rpc.ready().then(() => { void useConfigStore.getState().loadProviders(); });
	rpc.onConfigure(() => { void useConfigStore.getState().loadProviders(); });
});

const isSidebarMode = (window as any).__LIS_SIDEBAR_MODE === true;
const appRoot       = document.getElementById('app')!;

// Chat-panel mode needs the `init { chatId }` listener wired BEFORE we post
// `ready` — the host responds with `lis.init` + workspace + `init` in one
// burst, and if React hasn't mounted yet the `init` message lands without a
// listener and is silently dropped. Wiring here makes the bridge owner of
// init handling, with React-mount timing as an irrelevant detail.
if (!isSidebarMode) {
	installChatPanelInit();
}

/**
 * Mount sequence:
 *   1. Post `lis.ready` so the extension host pushes `lis.init` with config.
 *   2. Await `rpc.ready()` — resolves the moment the first non-empty config
 *      lands, so no store mounts against an unconfigured client.
 *   3. Render React + kick off the bootstrap REST calls.
 *
 * Showing a tiny placeholder while we wait keeps the webview visibly alive if
 * the host is slow (or never sends config, in which case the user sees the
 * first-run config notification from the extension host).
 */
async function boot(): Promise<void> {
	appRoot.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--vscode-descriptionForeground,#888);font:13px var(--vscode-font-family,system-ui)">Connecting to Lis…</div>`;
	vscode.postMessage({ type: isSidebarMode ? 'ready' : 'ready' });

	await rpc.ready();
	lisLog.info('webview: config ready — mounting React');

	// loadSessions has its own inflight-de-dup so the SessionSidebar's own
	// refreshGlobalSessions call won't double-fetch.
	void useGlobalSessionsStore.getState().loadSessions();

	if (isSidebarMode) {
		const { SidebarOCApp, installSidebarFrameHandler } = await import('./sidebarOC');
		installSidebarFrameHandler();
		createRoot(appRoot).render(createElement(SidebarOCApp, null));
	} else {
		createRoot(appRoot).render(createElement(App, null));
	}
}

void boot();
