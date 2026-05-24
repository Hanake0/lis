import * as vscode from 'vscode';
import { LisClient } from './lisClient';
import type { ServerFrame } from './types';
import { generateNonce } from './util';

export class SidebarWebviewProvider implements vscode.WebviewViewProvider {
	private view: vscode.WebviewView | null = null;
	private onOpenSession: ((chatId: string, title?: string) => void) | null = null;

	constructor(
		private readonly extensionUri: vscode.Uri,
		private readonly client: LisClient,
	) {
		client.on('frame', (frame: ServerFrame) => this.onFrame(frame));
	}

	setOpenSessionHandler(handler: (chatId: string, title?: string) => void): void {
		this.onOpenSession = handler;
	}

	resolveWebviewView(
		webviewView: vscode.WebviewView,
		_context: vscode.WebviewViewResolveContext,
		_token: vscode.CancellationToken,
	): void {
		this.view = webviewView;

		webviewView.webview.options = {
			enableScripts: true,
			localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'dist')],
		};

		webviewView.webview.html = this.getHtml(webviewView.webview);

		webviewView.webview.onDidReceiveMessage((msg) => {
			switch (msg.type) {
				case 'ready':
					if (this.client.connected) {
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
		const nonce = generateNonce();
		const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'dist', 'chat.css'));
		const jsUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'dist', 'sidebar.js'));
		const cspSource = webview.cspSource;

		return `<!DOCTYPE html>
<html lang="en" class="dark">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy"
		content="default-src 'none'; style-src ${cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: ${cspSource}; img-src data: ${cspSource} https:;">
	<link rel="stylesheet" href="${cssUri}">
	<style>html,body,#app{height:100%;width:100%;margin:0;padding:0;overflow:hidden;}</style>
</head>
<body>
	<div id="app"></div>
	<script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
	}
}
