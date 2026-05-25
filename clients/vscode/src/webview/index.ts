import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app';
import { vscode } from './vscodeApi';
import { LisRpcClient } from './lib/lis/rpc-client';
import { mountChatEventsBridge } from './sync/chat-events-bridge';

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
