import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app';
import { vscode } from './vscodeApi';
import { LisRpcClient } from './lib/lis/rpc-client';
import { mountChatEventsBridge } from './sync/chat-events-bridge';
import { useGlobalSessionsStore } from './stores/useGlobalSessionsStore';
import { installProjectsSync } from './stores/useProjectsStore';

// Install the RPC client BEFORE any store boots. `install()` registers the
// window message listener that translates the extension host's `lis.init` /
// `lis.config_update` postMessages into HTTP-client + SSE-channel config. The
// host sends `lis.init` in response to our `lis.ready` ping (set up by
// `install({ vscode })`). Every subsequent LisRpcClient.instance() call hits
// the configured client.
LisRpcClient.install(window, { vscode });

// Bridge global SSE chat-lifecycle events into useGlobalSessionsStore. Sidebar
// chat list stays in sync on create/update/delete/archive without an explicit
// refresh — the store mutations run as the events arrive on /api/events.
mountChatEventsBridge();

// Bootstrap the global sessions list as soon as the RPC client has a real
// origin to talk to. `onConfigure` fires synchronously when `lis.init` lands
// (or immediately if config is already cached from a previous setConfig call),
// so the sidebar populates without waiting for the SessionSidebar mount race.
// loadSessions has its own inflight-de-dup so the SessionSidebar's own
// refreshGlobalSessions call won't double-fetch.
LisRpcClient.instance().onConfigure(() => {
	void useGlobalSessionsStore.getState().loadSessions();
});

// Wire useProjectsStore: initial REST load on configure + live SSE updates for
// project_created/updated/deleted. Without this the sidebar falls back to the
// synthetic project hack and projects edited elsewhere never appear.
installProjectsSync();

const isSidebarMode = (window as any).__LIS_SIDEBAR_MODE === true;

if (isSidebarMode) {
	import('./sidebarOC').then(({ SidebarOCApp, installSidebarFrameHandler }) => {
		installSidebarFrameHandler();
		createRoot(document.getElementById('app')!).render(createElement(SidebarOCApp, null));
		vscode.postMessage({ type: 'ready' });
	});
} else {
	createRoot(document.getElementById('app')!).render(createElement(App, null));
	vscode.postMessage({ type: 'ready' });
}
