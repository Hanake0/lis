import * as vscode from 'vscode';

import type { LisConfig } from './config';
import { installHostFsHandler } from './hostFs';
import { type Logger, relayWebviewLog } from './logger';
import { buildWorkspaceMessage, openExternalPath, safeMtimeMs } from './sidebarWebviewProvider';
import { generateNonce } from './util';

interface WebviewInbound {
	type:                'ready' | 'lis.ready' | 'host_open_external' | 'add_project_committed' | 'close_add_project';
	path?:               string;
	label?:              string;
	color?:              string;
	default_path?:       string | null;
}

/**
 * Owns the singleton Add-Project picker WebviewPanel. Opens as an editor tab
 * (the only host surface that gives us room for a modal-feel card overlay);
 * the panel's React entry paints a full-area backdrop with the OC
 * DirectoryExplorerDialog centered on top. At most one panel exists at any
 * time — re-invoking the command focuses the existing tab.
 *
 * The host doesn't post chat-style `init` here — the picker is stateless
 * beyond the default path it seeds. The webview reads `lis.init` for HTTP
 * client config (used for `POST /api/projects` on commit) and posts back
 * `add_project_committed { path, label?, color? }` once the user picks. The
 * host disposes the panel after committing.
 */
export class AddProjectPanelManager {
	private static readonly viewType = 'lis.addProject';
	private panel: vscode.WebviewPanel | null = null;
	private defaultPath: string | null         = null;

	constructor(
		private readonly context:   vscode.ExtensionContext,
		private readonly getConfig: () => LisConfig,
		private readonly logger:    Logger | null = null,
	) {}

	/** Push the current config to the open picker (settings change). */
	notifyConfigChange(): void {
		if (!this.panel) return;
		const cfg = this.getConfig();
		this.panel.webview.postMessage({ type: 'lis.config_update', baseOrigin: cfg.baseOrigin, token: cfg.token, verbose: cfg.verbose });
	}

	notifyWorkspaceChange(): void {
		this.panel?.webview.postMessage(buildWorkspaceMessage());
	}

	/**
	 * Open the picker focused, or focus the existing tab. `defaultPath` seeds
	 * the initial directory shown by the dialog — falls through to the
	 * webview-side default (active project / first project / `~`) when null.
	 */
	openOrFocus(defaultPath: string | null = null): void {
		this.defaultPath = defaultPath;
		if (this.panel) {
			this.panel.reveal(undefined, false);
			// Re-push the default path in case the user picked something different
			// since the last open (e.g. clicked a different project's "+").
			this.panel.webview.postMessage({ type: 'add_project_default_path', default_path: defaultPath });
			return;
		}

		const panel = vscode.window.createWebviewPanel(
			AddProjectPanelManager.viewType,
			'Add Project',
			{ viewColumn: vscode.ViewColumn.Active, preserveFocus: false },
			{
				enableScripts:           true,
				retainContextWhenHidden: false,
				localResourceRoots: [
					vscode.Uri.joinPath(this.context.extensionUri, 'media'),
					vscode.Uri.joinPath(this.context.extensionUri, 'dist'),
				],
			},
		);
		panel.iconPath     = vscode.Uri.joinPath(this.context.extensionUri, 'media', 'lis-noturna.svg');
		panel.webview.html = this.buildHtml(panel.webview);
		this.panel         = panel;

		installHostFsHandler(panel.webview, (msg) => {
			if (msg?.type === 'lis.log') { relayWebviewLog(this.logger, msg); return; }
			this.onWebviewMessage(msg as WebviewInbound & { type: string });
		}, this.logger);

		panel.onDidDispose(() => {
			this.panel = null;
			this.logger?.info('addProject: panel disposed');
		});

		this.logger?.info(`addProject: opened (default=${defaultPath ?? '∅'})`);
	}

	private onWebviewMessage(msg: WebviewInbound & { type: string }): void {
		switch (msg.type) {
			case 'lis.ready':
			case 'ready': {
				const cfg = this.getConfig();
				this.panel?.webview.postMessage({ type: 'lis.init', baseOrigin: cfg.baseOrigin, token: cfg.token, verbose: cfg.verbose });
				this.panel?.webview.postMessage(buildWorkspaceMessage());
				this.panel?.webview.postMessage({ type: 'add_project_default_path', default_path: this.defaultPath });
				this.logger?.info(`addProject: handshake (${msg.type}) — pushed init + default=${this.defaultPath ?? '∅'}`);
				return;
			}

			case 'host_open_external':
				if (typeof msg.path === 'string' && msg.path.length > 0) void openExternalPath(msg.path);
				return;

			case 'close_add_project':
				this.logger?.info('addProject: close_add_project — disposing panel');
				this.panel?.dispose();
				return;

			case 'add_project_committed':
				// The webview already called POST /api/projects via LisRpcClient (it
				// holds the same config the sidebar does). All we do here is close
				// the picker — the sidebar's SSE subscription will surface the new
				// project automatically. Logged for traceability.
				this.logger?.info(`addProject: committed path=${msg.path ?? '∅'}`);
				this.panel?.dispose();
				return;
		}
	}

	private buildHtml(webview: vscode.Webview): string {
		const distRoot   = vscode.Uri.joinPath(this.context.extensionUri, 'dist');
		const cssDisk    = vscode.Uri.joinPath(distRoot, 'chat.css');
		const jsDisk     = vscode.Uri.joinPath(distRoot, 'addProject.js');
		// mtime cache-bust so a fresh esbuild rebuild lands in the webview without
		// a hard reload — same trick the chat panel + sidebar use.
		const cssUri     = `${webview.asWebviewUri(cssDisk)}?v=${safeMtimeMs(cssDisk.fsPath)}`;
		const jsUri      = `${webview.asWebviewUri(jsDisk)}?v=${safeMtimeMs(jsDisk.fsPath)}`;
		const nonce      = generateNonce();
		const cspSource  = webview.cspSource;
		const apiOrigin  = this.getConfig().baseOrigin;
		const connectSrc = apiOrigin ? `${cspSource} ${apiOrigin}` : cspSource;

		return `<!DOCTYPE html>
<html lang="en" class="dark">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy"
		content="default-src 'none'; style-src ${cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: ${cspSource}; img-src data: ${cspSource} https:; connect-src ${connectSrc};">
	<link rel="stylesheet" href="${cssUri}">
	<style>html,body,#app{height:100%;width:100%;margin:0;padding:0;overflow:hidden;}</style>
</head>
<body>
	<div id="app"></div>
	<script nonce="${nonce}">window.__LIS_ADD_PROJECT_MODE = true;</script>
	<script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
	}
}
