import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { AddProjectApp } from './addProjectApp';
import { vscode, installInboundMessageTracer } from './vscodeApi';
import { LisRpcClient } from './lib/lis/rpc-client';
import { lisLog } from './lib/lis/logger';
import { installProjectsSync } from './stores/useProjectsStore';

// Trace every inbound postMessage when verbose; wire BEFORE RPC install so
// the trace fires for `lis.init` itself.
installInboundMessageTracer();
lisLog.info('addProject webview: boot');

// Install the RPC client BEFORE any store boots so `lis.init` lands cleanly
// and downstream sync layers see real config from the first render.
const rpc = LisRpcClient.install(window, { vscode });

// useProjectsStore feeds DirectoryExplorerDialog (it reads `projects` to mark
// already-added paths with the "Added" badge, and it owns the `addProject`
// action the dialog calls on commit). Install before we await rpc.ready so
// the first `lis.config_update` triggers loadAll.
installProjectsSync();

const appRoot = document.getElementById('app')!;

/**
 * Mount sequence — identical to index.ts but tailored for the picker:
 *   1. Post `lis.ready` so the host pushes `lis.init`.
 *   2. Await `rpc.ready()` — resolves on first non-empty config so the
 *      dialog never mounts against an unconfigured client.
 *   3. Render the picker.
 */
async function boot(): Promise<void> {
    appRoot.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--vscode-descriptionForeground,#888);font:13px var(--vscode-font-family,system-ui)">Loading…</div>`;
    vscode.postMessage({ type: 'ready' });

    await rpc.ready();
    lisLog.info('addProject webview: config ready — mounting React');

    createRoot(appRoot).render(createElement(AddProjectApp, null));
}

void boot();
