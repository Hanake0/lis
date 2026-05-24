import * as vscode from 'vscode';

import type { LisConfig } from './config';
import type { LisClient } from './lisClient';
import type { ServerFrame } from './types';
import { generateNonce } from './util';

/**
 * Provides the activity-bar sidebar webview. The webview talks REST + SSE
 * directly to the Lis backend — the extension host only ships config and
 * legacy WS frames through postMessage.
 */
export class SidebarWebviewProvider implements vscode.WebviewViewProvider {
	private view: vscode.WebviewView | null = null;
	private onOpenSession: ((chatId: string, title?: string) => void) | null = null;

	constructor(
		private readonly extensionUri: vscode.Uri,
		private readonly client:       LisClient,
		private          getConfig:    () => LisConfig,
	) {
		client.onFrame((frame: ServerFrame) => this.onFrame(frame));
	}

	setOpenSessionHandler(handler: (chatId: string, title?: string) => void): void {
		this.onOpenSession = handler;
	}

	/** Called from extension.ts when `lis.serverUrl` / `lis.authToken` change at runtime. */
	notifyConfigChange(): void {
		const cfg = this.getConfig();
		this.post({ type: 'lis.config_update', baseOrigin: cfg.baseOrigin, token: cfg.token });
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
				case 'lis.ready': {
					// New webview booted; push the current config so the LisRpcClient can wire up.
					const cfg = this.getConfig();
					this.post({ type: 'lis.init', baseOrigin: cfg.baseOrigin, token: cfg.token });
					return;
				}

				case 'ready':
					if (this.client.currentState === 'connected') {
						this.post({ type: 'state', state: 'connected' });
						this.client.send({ type: 'list_chats' });
						this.client.send({ type: 'list_agents' });
					}
					break;

				case 'open_session':
					this.onOpenSession?.(msg.chat_id, msg.title);
					break;

				case 'create_session':
					if (msg.chat_id) {
						this.client.send({ type: 'create_session', chat_id: msg.chat_id, name: msg.name });
						this.onOpenSession?.(msg.chat_id, msg.name);
					}
					break;

				case 'delete_session':
					if (msg.chat_id) this.client.send({ type: 'delete_session', chat_id: msg.chat_id });
					break;

				case 'rename_session':
					if (msg.chat_id && msg.name) this.client.send({ type: 'rename_session', chat_id: msg.chat_id, name: msg.name });
					break;

				case 'archive_session':
					if (msg.chat_id) this.client.send({ type: 'archive_session', chat_id: msg.chat_id });
					break;

				case 'send_frame':
					if (msg.frame) this.client.send(msg.frame);
					break;
			}
		});
	}

	private onFrame(frame: ServerFrame): void {
		if (frame.type === 'connected') {
			this.post({ type: 'state', state: 'connected' });
			this.client.send({ type: 'list_chats' });
			return;
		}

		if (frame.type === 'chat_list' || frame.type === 'chat_created'
			|| frame.type === 'chat_deleted' || frame.type === 'chat_renamed'
			|| frame.type === 'chat_archived' || frame.type === 'agent_list') {
			this.post({ type: 'frame', frame });
		}
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
