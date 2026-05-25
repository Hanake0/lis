import * as vscode from 'vscode';

import type { LisConfig } from './config';
import { generateNonce } from './util';

/**
 * Provides the activity-bar sidebar webview. The webview talks REST + SSE
 * directly to the Lis backend — the extension host only ships config + workspace
 * hints through postMessage. There is no WebSocket and no frame proxying.
 */
export class SidebarWebviewProvider implements vscode.WebviewViewProvider {
	private view: vscode.WebviewView | null = null;
	private onOpenSession: ((chatId: string, title?: string) => void) | null = null;

	constructor(
		private readonly extensionUri: vscode.Uri,
		private          getConfig:    () => LisConfig,
	) {}

	setOpenSessionHandler(handler: (chatId: string, title?: string) => void): void {
		this.onOpenSession = handler;
	}

	/** Called from extension.ts when `lis.serverUrl` / `lis.authToken` change at runtime. */
	notifyConfigChange(): void {
		const cfg = this.getConfig();
		this.post({ type: 'lis.config_update', baseOrigin: cfg.baseOrigin, token: cfg.token });
	}

	/** Called on activation + when the VS Code workspace folders change. */
	notifyWorkspaceChange(): void {
		this.post(buildWorkspaceMessage());
	}

	resolveWebviewView(
		webviewView: vscode.WebviewView,
		_context: vscode.WebviewViewResolveContext,
		_token: vscode.CancellationToken,
	): void {
		this.view = webviewView;

		webviewView.webview.options = {
			enableScripts:       true,
			localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'dist')],
		};

		webviewView.webview.html = this.getHtml(webviewView.webview);

		webviewView.webview.onDidReceiveMessage((msg) => {
			switch (msg.type) {
				case 'lis.ready':
				case 'ready': {
					// Both events come from the webview's React mount — push the current
					// config so LisRpcClient can wire up, plus the workspace info so
					// useDirectoryStore mirrors the host.
					const cfg = this.getConfig();
					this.post({ type: 'lis.init', baseOrigin: cfg.baseOrigin, token: cfg.token });
					this.post(buildWorkspaceMessage());
					return;
				}

				case 'host_open_external':
					if (typeof msg.path === 'string' && msg.path.length > 0) void openExternalPath(msg.path);
					return;

				case 'open_session':
					this.onOpenSession?.(msg.chat_id, msg.title);
					return;
			}
		});
	}

	private post(msg: unknown): void {
		this.view?.webview.postMessage(msg);
	}

	private getHtml(webview: vscode.Webview): string {
		const nonce     = generateNonce();
		const cssUri    = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'dist', 'chat.css'));
		const jsUri     = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'dist', 'chat.js'));
		const cspSource = webview.cspSource;
		const apiOrigin = this.getConfig().baseOrigin;
		// CSP connect-src must include the user-configured backend so fetch() + EventSource
		// can reach /api/* directly. If empty, only the extension's own resources are allowed.
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
	<script nonce="${nonce}">window.__LIS_SIDEBAR_MODE = true;</script>
	<script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
	}
}

/**
 * Build the `lis.workspace` envelope sent to webviews so their `useDirectoryStore`
 * can mirror the host's workspace + home directory. Pulled out of the provider
 * so the chat-panel path can reuse it without duplicating the os/vscode wiring.
 */
export function buildWorkspaceMessage(): { type: 'lis.workspace'; workspaceRoot?: string; homeDirectory: string } {
	const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
	// Read home from process.env so we don't need a `node:os` import (esbuild's
	// `platform: 'browser'` config can't resolve `node:*` modules). The vars
	// are populated by Node in the extension host.
	const env  = (typeof process !== 'undefined' && process.env) ? process.env : {} as NodeJS.ProcessEnv;
	const home = env.HOME ?? env.USERPROFILE ?? (env.HOMEDRIVE && env.HOMEPATH ? `${env.HOMEDRIVE}${env.HOMEPATH}` : '');
	return {
		type:          'lis.workspace',
		workspaceRoot: folder,
		homeDirectory: home,
	};
}

/**
 * Resolve a path or URL into a VS Code action. Webview dispatch from
 * `openDesktopPath` / `openDesktopFileInApp` in lib/desktop.ts.
 *
 * - `http(s)://` / `vscode://` / `mailto:` etc. → `vscode.env.openExternal`
 * - Local file/folder paths → open the parent in the OS file explorer via
 *   `revealFileInOS` if it's a file, or open the folder in a new window if
 *   it's a directory.
 */
export async function openExternalPath(target: string): Promise<void> {
	const trimmed = target.trim();
	if (!trimmed) return;

	// Try URI schemes first — http(s), vscode://, mailto:, etc.
	try {
		const uri = vscode.Uri.parse(trimmed, true);
		if (uri.scheme && uri.scheme !== 'file' && uri.scheme !== '') {
			await vscode.env.openExternal(uri);
			return;
		}
	} catch { /* not a URI — fall through to file handling */ }

	// Path on disk — try to reveal it; if it's a directory, open in a new window.
	try {
		const uri  = vscode.Uri.file(trimmed);
		const stat = await vscode.workspace.fs.stat(uri);
		if (stat.type === vscode.FileType.Directory) {
			await vscode.commands.executeCommand('vscode.openFolder', uri, { forceNewWindow: true });
		} else {
			await vscode.commands.executeCommand('revealFileInOS', uri);
		}
	} catch (err) {
		console.warn('[lis] openExternalPath failed:', trimmed, err);
	}
}
