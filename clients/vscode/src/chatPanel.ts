import * as vscode from 'vscode';

import { LisClient } from './lisClient';
import type { ConnectionState, ServerFrame } from './types';
import { generateNonce } from './util';

interface WebviewInbound {
	type:           'ready' | 'send' | 'send_frame';
	text?:          string;
	model?:         string;
	effort?:        string;
	tool_profile?:  string;
	thinking_mode?: string;
	frame?:         Record<string, unknown>;
}

interface ChatPanel {
	readonly chatId: string;
	title:    string;
	panel:    vscode.WebviewPanel;
	ready:    boolean;
}

/**
 * Owns one editor WebviewPanel per chat_id. Open or focus by chat_id; the panel
 * stays alive until the user closes it. Frames from the shared LisClient are
 * filtered by chat_id and forwarded only to the matching panel.
 */
export class ChatPanelManager {
	private static readonly viewType = 'lis.chatPanel';
	private readonly panels = new Map<string, ChatPanel>();

	constructor(
		private readonly context: vscode.ExtensionContext,
		private readonly client:  LisClient,
	) {
		client.onFrame(frame => this.onFrame(frame));
		client.onState(state => this.broadcastState(state));
	}

	openOrFocus(chatId: string, title?: string): void {
		const existing = this.panels.get(chatId);
		if (existing) {
			if (title && title !== existing.title) {
				existing.title = title;
				existing.panel.title = title;
			}
			existing.panel.reveal(undefined, true);
			return;
		}

		const resolvedTitle = title && title.length > 0 ? title : 'Chat';
		const panel = vscode.window.createWebviewPanel(
			ChatPanelManager.viewType,
			resolvedTitle,
			{ viewColumn: vscode.ViewColumn.Active, preserveFocus: false },
			{
				enableScripts:   true,
				retainContextWhenHidden: true,
				localResourceRoots: [
						vscode.Uri.joinPath(this.context.extensionUri, 'media'),
						vscode.Uri.joinPath(this.context.extensionUri, 'dist'),
					],
			},
		);
		panel.iconPath = vscode.Uri.joinPath(this.context.extensionUri, 'media', 'lis-noturna.svg');
		panel.webview.html = this.buildHtml(panel.webview);

		const entry: ChatPanel = { chatId, title: resolvedTitle, panel, ready: false };
		this.panels.set(chatId, entry);

		panel.webview.onDidReceiveMessage((msg: WebviewInbound) => this.onWebviewMessage(entry, msg));
		panel.onDidDispose(() => {
			this.panels.delete(chatId);
		});
	}

	private onWebviewMessage(entry: ChatPanel, msg: WebviewInbound): void {
		switch (msg.type) {
			case 'ready':
				entry.ready = true;
				this.post(entry, { type: 'init', chatId: entry.chatId });
				this.post(entry, { type: 'state', state: this.client.currentState });
				this.client.send({ type: 'history', chat_id: entry.chatId });
				this.client.send({ type: 'get_agent', chat_id: entry.chatId });
				this.client.send({ type: 'list_agents' });
				break;

			case 'send':
				if (!msg.text) return;
				this.client.send({
					type: 'message', chat_id: entry.chatId, text: msg.text, dispatch: 'immediate',
					model: msg.model, effort: msg.effort, tool_profile: msg.tool_profile,
					thinking_mode: msg.thinking_mode,
				});
				break;

			case 'send_frame':
				if (msg.frame) this.client.send(msg.frame as never);
				break;
		}
	}

	private onFrame(frame: ServerFrame): void {
		if (frame.type === 'chat_list' || frame.type === 'connected') return;

		// agent_list is global — broadcast to all panels
		if (frame.type === 'agent_list') {
			for (const entry of this.panels.values()) this.post(entry, { type: 'frame', frame });
			return;
		}

		const chatId = (frame as { chat_id?: string }).chat_id;
		if (!chatId) return;

		const entry = this.panels.get(chatId);
		if (entry) this.post(entry, { type: 'frame', frame });
	}

	private broadcastState(state: ConnectionState): void {
		for (const entry of this.panels.values()) this.post(entry, { type: 'state', state });
	}

	private post(entry: ChatPanel, msg: unknown): void {
		if (!entry.ready && (msg as { type?: string }).type !== 'init') {
			// Queue a small grace window? In practice 'ready' fires within a few ms; drop is fine.
		}
		entry.panel.webview.postMessage(msg);
	}

	private buildHtml(webview: vscode.Webview): string {
		const mediaRoot = vscode.Uri.joinPath(this.context.extensionUri, 'media');
		const distRoot  = vscode.Uri.joinPath(this.context.extensionUri, 'dist');
		const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(mediaRoot, 'chat.css'));
		const jsUri  = webview.asWebviewUri(vscode.Uri.joinPath(distRoot, 'chat.js'));
		const nonce  = generateNonce();

		return BUNDLED_HTML
			.replace(/\$\{nonce\}/g,     nonce)
			.replace(/\$\{cspSource\}/g, webview.cspSource)
			.replace(/\$\{cssUri\}/g,    cssUri.toString())
			.replace(/\$\{jsUri\}/g,     jsUri.toString());
	}
}

const BUNDLED_HTML = `<!DOCTYPE html>
<html lang="en">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src \${cspSource} 'unsafe-inline'; script-src 'nonce-\${nonce}';">
	<link rel="stylesheet" href="\${cssUri}">
	<title>Lis Chat</title>
</head>
<body>
	<div id="app"></div>
	<script nonce="\${nonce}" src="\${jsUri}"></script>
</body>
</html>`;

